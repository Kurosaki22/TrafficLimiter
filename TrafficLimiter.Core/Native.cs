using System.Runtime.InteropServices;
using System.Text;

namespace TrafficLimiter.Core;

static class WinDivert
{
    public const int AddrSize = 80;               // sizeof(WINDIVERT_ADDRESS) in 2.2
    public const int LayerNetwork = 0, LayerFlow = 2;
    public const ulong FlagSniff = 1, FlagRecvOnly = 4;
    public const int ShutdownBoth = 3;

    [DllImport("WinDivert.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);
    [DllImport("WinDivert.dll", SetLastError = true, EntryPoint = "WinDivertRecv")]
    public static extern bool Recv(IntPtr h, byte[] pkt, uint len, out uint recvLen, byte[] addr);
    [DllImport("WinDivert.dll", SetLastError = true, EntryPoint = "WinDivertRecv")]
    public static extern bool RecvEvent(IntPtr h, IntPtr pkt, uint len, IntPtr recvLen, byte[] addr);
    [DllImport("WinDivert.dll", SetLastError = true, EntryPoint = "WinDivertSend")]
    public static extern bool Send(IntPtr h, byte[] pkt, uint len, out uint sendLen, byte[] addr);
    [DllImport("WinDivert.dll", SetLastError = true)]
    public static extern bool WinDivertShutdown(IntPtr h, int how);
    [DllImport("WinDivert.dll", SetLastError = true)]
    public static extern bool WinDivertClose(IntPtr h);
}

/// <summary>Prozess-Zuordnung über iphlpapi (bestehende Verbindungen) und Pfadauflösung.</summary>
static class Win32Info
{
    [DllImport("iphlpapi.dll")]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int af, int cls, uint reserved);
    [DllImport("iphlpapi.dll")]
    static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool sort, int af, int cls, uint reserved);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

    public static int Key(byte proto, int port) => proto << 16 | port;

    /// <summary>(proto,lokaler Port) -> PID aller aktuellen TCP/UDP-Sockets.</summary>
    public static void ReadTables(IDictionary<int, int> map)
    {
        // Parameter: TCP?, AF, Zeilengröße, Offset lokaler Port, Offset PID
        Read(false, 2, 12, 4, 8, map); Read(false, 23, 28, 20, 24, map);
        Read(true, 2, 24, 8, 20, map); Read(true, 23, 56, 20, 52, map);
    }

    static void Read(bool tcp, int af, int rowSize, int portOff, int pidOff, IDictionary<int, int> map)
    {
        int size = 0;
        Call(tcp, IntPtr.Zero, ref size, af);
        for (int i = 0; i < 3; i++) // Tabelle kann zwischen den Aufrufen wachsen
        {
            size += 4096;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                if (Call(tcp, buf, ref size, af) != 0) continue;
                int n = Marshal.ReadInt32(buf);
                for (int r = 0; r < n; r++)
                {
                    var row = buf + 4 + r * rowSize;
                    int p = Marshal.ReadInt32(row, portOff) & 0xFFFF;
                    p = (p & 0xFF) << 8 | p >> 8; // network -> host
                    map[Key(tcp ? (byte)6 : (byte)17, p)] = Marshal.ReadInt32(row, pidOff);
                }
                return;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
    }

    // TCP_TABLE_OWNER_PID_ALL = 5, UDP_TABLE_OWNER_PID = 1
    static uint Call(bool tcp, IntPtr buf, ref int size, int af) => tcp
        ? GetExtendedTcpTable(buf, ref size, false, af, 5, 0)
        : GetExtendedUdpTable(buf, ref size, false, af, 1, 0);

    /// <summary>Voller Pfad (PROCESS_QUERY_LIMITED_INFORMATION), null wenn nicht lesbar.</summary>
    public static string? ImagePath(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int n = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }
}
