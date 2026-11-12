using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace TrafficLimiter.Core;

public record AppSnapshot(string Path, string Name, long DownRate, long UpRate, long TotalDown, long TotalUp);

/// <summary>
/// Hält Pakete per WinDivert an (NETWORK-Layer), ordnet sie über FLOW-Events + iphlpapi einem Prozess zu,
/// zählt Traffic und wendet Block/Limit an. Pakete ohne bekannte Zuordnung werden unverändert durchgelassen.
/// </summary>
public sealed class PacketEngine : IDisposable
{
    sealed class Stats { public long Down, Up, LastDown, LastUp; public string Name = ""; }
    sealed class Delayed { public byte[] Data = []; public uint Len; public byte[] Addr = []; }

    const double MaxDelaySeconds = 1.0;     // länger warten = droppen (TCP/QUIC bremst dann selbst)
    const long MaxQueuedBytes = 16 << 20;

    readonly ConcurrentDictionary<int, int> _flows = new();                       // proto<<16|lokaler Port -> PID
    readonly ConcurrentDictionary<int, (string Path, string Name)> _procs = new();
    readonly ConcurrentDictionary<string, Stats> _stats = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<(string, bool), TokenBucket> _buckets = new();
    readonly TokenBucket _globalDown = new(1, 0), _globalUp = new(1, 0);
    readonly PriorityQueue<Delayed, double> _queue = new();
    readonly object _qLock = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    volatile Dictionary<string, AppRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    IntPtr _net = -1, _flow = -1;
    Thread[] _threads = [];
    long _queuedBytes, _lastSnapshotTicks, _lastTableRead;
    volatile bool _running;

    public bool Running => _running;
    public bool Paused { get; set; }
    public long GlobalDownLimit { get; set; }   // Bytes/s, 0 = aus
    public long GlobalUpLimit { get; set; }

    double Now => _clock.Elapsed.TotalSeconds;

    public void SetRules(IEnumerable<AppRule> rules) =>
        _rules = rules.Where(r => r.Enabled).ToDictionary(r => r.AppPath, StringComparer.OrdinalIgnoreCase);

    public void Start()
    {
        if (_running) return;
        _flow = WinDivert.WinDivertOpen("true", WinDivert.LayerFlow, 0, WinDivert.FlagSniff | WinDivert.FlagRecvOnly);
        if (_flow == -1) throw new Win32Exception(); // meist: nicht als Admin gestartet
        ReadTables();
        _net = WinDivert.WinDivertOpen("!loopback and (tcp or udp)", WinDivert.LayerNetwork, 0, 0);
        if (_net == -1) { var e = new Win32Exception(); WinDivert.WinDivertClose(_flow); throw e; }
        _running = true;
        _threads = [new(FlowLoop), new(PacketLoop), new(SendLoop)];
        foreach (var t in _threads) { t.IsBackground = true; t.Start(); }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        WinDivert.WinDivertShutdown(_net, WinDivert.ShutdownBoth);
        WinDivert.WinDivertShutdown(_flow, WinDivert.ShutdownBoth);
        lock (_qLock) Monitor.PulseAll(_qLock);
        foreach (var t in _threads) t.Join(2000);
        WinDivert.WinDivertClose(_net);
        WinDivert.WinDivertClose(_flow);
        lock (_qLock) { _queue.Clear(); _queuedBytes = 0; }
    }

    public void Dispose() => Stop();

    /// <summary>Aktuelle Raten (seit letztem Aufruf) und Summen pro Anwendung.</summary>
    public List<AppSnapshot> Snapshot()
    {
        long t = Stopwatch.GetTimestamp();
        double dt = Math.Max((t - _lastSnapshotTicks) / (double)Stopwatch.Frequency, 0.001);
        _lastSnapshotTicks = t;
        var list = new List<AppSnapshot>();
        foreach (var (path, s) in _stats)
        {
            long d = Volatile.Read(ref s.Down), u = Volatile.Read(ref s.Up);
            list.Add(new(path, s.Name, (long)((d - s.LastDown) / dt), (long)((u - s.LastUp) / dt), d, u));
            s.LastDown = d; s.LastUp = u;
        }
        return list;
    }

    void ReadTables()
    {
        Win32Info.ReadTables(_flows);
        _lastTableRead = Environment.TickCount64;
    }

    void FlowLoop()
    {
        var addr = new byte[WinDivert.AddrSize];
        while (_running)
        {
            if (!WinDivert.RecvEvent(_flow, IntPtr.Zero, 0, IntPtr.Zero, addr)) { if (_running) continue; break; }
            // WINDIVERT_ADDRESS: Event @9, Flow.ProcessId @32, LocalPort @68, Protocol @72
            byte ev = addr[9], proto = addr[72];
            int port = BinaryPrimitives.ReadUInt16LittleEndian(addr.AsSpan(68));
            int pid = (int)BinaryPrimitives.ReadUInt32LittleEndian(addr.AsSpan(32));
            int key = Win32Info.Key(proto, port);
            // shortcut: Schlüssel ohne lokale Adresse, Kollision nur bei gleichem Port auf mehreren lokalen IPs
            if (ev == 1) { _flows[key] = pid; _procs.TryRemove(pid, out _); }  // ESTABLISHED (Pfad neu auflösen)
            else if (ev == 2) _flows.TryRemove(new KeyValuePair<int, int>(key, pid)); // DELETED
        }
    }

    (string Path, string Name) Resolve(int pid) => _procs.GetOrAdd(pid, p =>
    {
        if (p is 0 or 4) return ("System", "System");
        var path = Win32Info.ImagePath(p);
        if (path != null) return (path, System.IO.Path.GetFileName(path));
        string name = "?";
        try { name = Process.GetProcessById(p).ProcessName; } catch { }
        return ($"[{name}]", name);
    });

    bool TryPid(byte proto, int port, out int pid)
    {
        int key = Win32Info.Key(proto, port);
        if (_flows.TryGetValue(key, out pid)) return true;
        // Verbindung vor Engine-Start/ohne Event: Tabelle neu lesen, höchstens alle 2 s
        if (Environment.TickCount64 - _lastTableRead > 2000) ReadTables();
        return _flows.TryGetValue(key, out pid);
    }

    void PacketLoop()
    {
        var buf = new byte[65535 + 64];
        var addr = new byte[WinDivert.AddrSize];
        while (_running)
        {
            if (!WinDivert.Recv(_net, buf, (uint)buf.Length, out uint len, addr)) { if (_running) continue; break; }
            double delay = Handle(buf, (int)len, addr, out bool drop);
            if (drop) continue;
            if (delay <= 0) { WinDivert.Send(_net, buf, len, out _, addr); continue; }
            var d = new Delayed { Data = buf.AsSpan(0, (int)len).ToArray(), Len = len, Addr = (byte[])addr.Clone() };
            lock (_qLock)
            {
                if (_queuedBytes + len > MaxQueuedBytes) continue;
                _queuedBytes += len;
                _queue.Enqueue(d, Now + delay);
                Monitor.Pulse(_qLock);
            }
        }
    }

    /// <summary>Zählt das Paket und liefert die nötige Wartezeit; drop=true: verwerfen.</summary>
    double Handle(byte[] p, int len, byte[] addr, out bool drop)
    {
        drop = false;
        bool outbound = (addr[10] & 2) != 0; // Outbound-Bit
        int ihl, proto;
        if (len >= 20 && p[0] >> 4 == 4) { ihl = (p[0] & 15) * 4; proto = p[9]; }
        else if (len >= 40 && p[0] >> 4 == 6) { ihl = 40; proto = p[6]; } // shortcut: IPv6-Extension-Header -> durchlassen
        else return 0;
        if ((proto != 6 && proto != 17) || len < ihl + 4) return 0;
        int src = p[ihl] << 8 | p[ihl + 1], dst = p[ihl + 2] << 8 | p[ihl + 3];
        if (!TryPid((byte)proto, outbound ? src : dst, out int pid)) return 0;

        var (path, name) = Resolve(pid);
        var st = _stats.GetOrAdd(path, _ => new Stats { Name = name });
        var rule = _rules.GetValueOrDefault(path);
        if (!Paused && rule is { Block: true }) { drop = true; return 0; }

        if (outbound) Interlocked.Add(ref st.Up, len); else Interlocked.Add(ref st.Down, len);
        if (Paused) return 0;

        double now = Now, delay = 0;
        long limit = rule == null ? 0 : outbound ? rule.UpLimit : rule.DownLimit;
        if (limit > 0)
        {
            var b = _buckets.GetOrAdd((path, outbound), _ => new TokenBucket(limit, now));
            if ((long)b.Rate != limit) b.SetRate(limit, now);
            delay = b.Delay(len, now);
        }
        long cap = outbound ? GlobalUpLimit : GlobalDownLimit;
        if (cap > 0)
        {
            var g = outbound ? _globalUp : _globalDown;
            if ((long)g.Rate != cap) g.SetRate(cap, now);
            // Priorität: höhere Stufen dürfen mehr vom Bucket verbrauchen, niedrigere warten zuerst
            double reserve = (rule?.Priority ?? Priority.Normal) switch
            { Priority.Critical => 0, Priority.High => 0.1, Priority.Normal => 0.25, _ => 0.5 };
            delay = Math.Max(delay, g.Delay(len, now, reserve));
        }
        if (delay > MaxDelaySeconds) { drop = true; return 0; }
        return delay;
    }

    void SendLoop()
    {
        while (_running)
        {
            Delayed d;
            lock (_qLock)
            {
                if (!_queue.TryPeek(out _, out double due)) { Monitor.Wait(_qLock, 100); continue; }
                double wait = due - Now;
                if (wait > 0) { Monitor.Wait(_qLock, TimeSpan.FromSeconds(Math.Min(wait, 0.1))); continue; }
                d = _queue.Dequeue();
                _queuedBytes -= d.Len;
            }
            WinDivert.Send(_net, d.Data, d.Len, out _, d.Addr);
        }
    }
}
