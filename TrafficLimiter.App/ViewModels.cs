using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrafficLimiter.Core;

namespace TrafficLimiter.App;

/// <summary>Ein Messpunkt: Zeit in Sekunden auf <see cref="Clock"/>, Raten in Bytes/s.</summary>
public readonly record struct Sample(double T, long Down, long Up);

public static class Clock
{
    static readonly Stopwatch Watch = Stopwatch.StartNew();
    public static double Now => Watch.Elapsed.TotalSeconds;
}

public static class Fmt
{
    public static string Bytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.##} GB",
        >= 1 << 20 => $"{b / (double)(1 << 20):0.##} MB",
        >= 1 << 10 => $"{b / 1024.0:0.#} KB",
        _ => $"{b} B"
    };
}

/// <summary>Bytes -> Text; ConverterParameter "rate" hängt "/s" an, 0 wird leer.</summary>
public class BytesConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        long b = System.Convert.ToInt64(value);
        if (b == 0 && p is "rate") return "–";
        return Fmt.Bytes(b) + (p is "rate" ? "/s" : "");
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Limit in KB/s: 0 wird als ∞ angezeigt, Eingabe "∞"/leer/ungültig = 0.</summary>
public class LimitConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        System.Convert.ToInt64(value) == 0 ? (p is "blank" ? "" : "∞") : value.ToString()!;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        long.TryParse(value as string, out long v) && v > 0 ? v : 0L;
}

public partial class AppRow : ObservableObject
{
    public const int HistoryLen = 120;     // 60 s bei 0,5 s Takt

    public AppRow(string path, string name)
    {
        Path = path; Name = name; Title = ReadTitle(path, name);
        Loc.Instance.Changed += () => OnPropertyChanged(nameof(RuleSummary)); // shortcut: Zeilen bleiben registriert, bei wenigen Dutzend Apps unkritisch
    }

    public string Path { get; }
    public string Name { get; }
    /// <summary>Lesbarer Name (Datei-Beschreibung, z. B. "Hostprozess für Windows-Dienste"), sonst der EXE-Name.</summary>
    public string Title { get; }
    public string ExeHint => Title != Name ? Name : "";
    public bool IsTotal { get; init; }     // Sammelzeile "Computer" (Summen + Gesamtlimit)
    public Action? Edited;                 // nur bei Benutzeränderungen von Limit/Block/Priorität
    public readonly Queue<Sample> History = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(TotalRate))] long downRate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TotalRate))] long upRate;
    [ObservableProperty] long totalDown;
    [ObservableProperty] long totalUp;
    [ObservableProperty] long peakDown;
    [ObservableProperty] long peakUp;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasLimit))] long downLimitKb;   // KB/s, 0 = unbegrenzt
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasLimit))] long upLimitKb;
    [ObservableProperty] bool isActive;   // Traffic in den letzten ~2 s (Hysterese gegen Flackern)
    [ObservableProperty] bool block;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RuleSummary))] Priority priority = Priority.Normal;

    public long TotalRate => DownRate + UpRate;
    public bool HasLimit => DownLimitKb > 0 || UpLimitKb > 0;

    int _idleTicks = 99;

    static string ReadTitle(string path, string name)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            var t = !string.IsNullOrWhiteSpace(v.FileDescription) ? v.FileDescription : v.ProductName;
            return string.IsNullOrWhiteSpace(t) ? name : t.Trim();
        }
        catch { return name; } // Pfad ungültig ("System", "[x]") oder Datei weg
    }

    /// <summary>Kurztext für die Liste: Priorität, falls nicht Normal (Limits haben eigene Spalten).</summary>
    public string RuleSummary => Priority != Priority.Normal ? Loc.T("prio_" + Priority) : "";

    partial void OnDownLimitKbChanged(long value) => Edited?.Invoke();
    partial void OnUpLimitKbChanged(long value) => Edited?.Invoke();
    partial void OnBlockChanged(bool value) => Edited?.Invoke();
    partial void OnPriorityChanged(Priority value) => Edited?.Invoke();

    ImageSource? _icon; bool _iconLoaded;
    public ImageSource? Icon
    {
        get
        {
            if (_iconLoaded) return _icon;
            _iconLoaded = true;
            try
            {
                using var ic = System.Drawing.Icon.ExtractAssociatedIcon(Path);
                if (ic != null)
                {
                    _icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    _icon.Freeze();
                }
            }
            catch { } // Pfad ungültig (z. B. "[System]") -> kein Icon
            return _icon;
        }
    }

    public AppRule ToRule() => new(Path, DownLimitKb * 1024, UpLimitKb * 1024, Block, Priority);
    public bool IsDefault => DownLimitKb == 0 && UpLimitKb == 0 && !Block && Priority == Priority.Normal;

    public void AddSample(Sample s)
    {
        History.Enqueue(s);
        if (History.Count > HistoryLen) History.Dequeue();
        _idleTicks = s.Down + s.Up > 0 ? 0 : _idleTicks + 1;
        IsActive = _idleTicks < 4;
        PeakDown = Math.Max(PeakDown, s.Down);
        PeakUp = Math.Max(PeakUp, s.Up);
    }
}

/// <summary>Computer-Zeile zuerst, danach alphabetisch nach angezeigtem Titel.</summary>
sealed class RowComparer : System.Collections.IComparer
{
    public int Compare(object? a, object? b)
    {
        var x = (AppRow)a!; var y = (AppRow)b!;
        if (x.IsTotal != y.IsTotal) return x.IsTotal ? -1 : 1;
        return StringComparer.CurrentCultureIgnoreCase.Compare(x.Title, y.Title);
    }
}

public partial class MainViewModel : ObservableObject
{
    public static Priority[] Priorities { get; } = Enum.GetValues<Priority>();
    static string T(string key) => Loc.T(key);

    readonly PacketEngine _engine = new();
    readonly Dictionary<string, AppRow> _map = new(StringComparer.OrdinalIgnoreCase);
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly string _file = RuleStore.DefaultPath;
    bool _loading = true, _noAdmin, _engineEnabled = true, _syncingAutostart;

    public AppRow TotalRow { get; } = new("", Environment.MachineName) { IsTotal = true };
    public ObservableCollection<AppRow> Rows { get; } = [];
    public ICollectionView View { get; }

    [ObservableProperty] AppRow? selected;
    [ObservableProperty] string search = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Status), nameof(EngineButton))] bool engineOn;
    [ObservableProperty] bool paused;
    [ObservableProperty] long globalDownKb;
    [ObservableProperty] long globalUpKb;
    [ObservableProperty] string totals = "";
    [ObservableProperty] string downText = "–";
    [ObservableProperty] string upText = "–";
    [ObservableProperty] Sample[] chartSamples = [];
    [ObservableProperty] bool processesExpanded = true;
    [ObservableProperty] string language = "auto";     // "auto" | "de" | "en"
    [ObservableProperty] bool startMinimized;
    [ObservableProperty] bool autostart;
    [ObservableProperty] bool showChart = true;
    [ObservableProperty] bool onlyActive;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProfileButton))] string? activeProfile;

    public List<Profile> Profiles { get; } = [];

    public string Status => EngineOn ? T("status_on") : _noAdmin ? T("status_noadmin") : T("status_off");
    public string EngineButton => EngineOn ? T("btn_stop") : T("btn_start");
    public string ChartTitle => (Selected ?? TotalRow).Title;
    public string ProfileButton => $"{T("profile_label")}: {ActiveProfile ?? T("profile_none")}  ▾";

    public MainViewModel()
    {
        var v = new ListCollectionView(Rows) { IsLiveFiltering = true, CustomSort = new RowComparer() };
        v.LiveFilteringProperties.Add(nameof(AppRow.IsActive));
        // Computer-Zeile immer sichtbar; Prozesse nur wenn aufgeklappt oder gesucht wird, optional nur aktive
        v.Filter = o => o is AppRow r && (r.IsTotal || (ProcessesExpanded || Search != "") && (!OnlyActive || r.IsActive)
            && (r.Title.Contains(Search, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)));
        View = v;
        Rows.Add(TotalRow);
        TotalRow.Edited = () => { GlobalDownKb = TotalRow.DownLimitKb; GlobalUpKb = TotalRow.UpLimitKb; };

        var s = RuleStore.Load(_file);
        foreach (var r in s.Rules)
            GetRow(r.AppPath, System.IO.Path.GetFileName(r.AppPath), r);
        GlobalDownKb = s.GlobalDownLimit / 1024;
        GlobalUpKb = s.GlobalUpLimit / 1024;
        Language = s.Language; StartMinimized = s.StartMinimized; Autostart = s.Autostart;
        _engineEnabled = s.EngineEnabled; ShowChart = s.ShowChart;
        Profiles.AddRange(s.Profiles ?? []); ActiveProfile = s.ActiveProfile;
        Loc.Instance.Set(Language);
        Loc.Instance.Changed += () => OnPropertyChanged(string.Empty); // alle Texte neu lesen
        _loading = false;
        Apply();
        Selected = TotalRow;

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        if (_engineEnabled) StartEngine(interactive: false); // letzter Zustand, Standard: an
    }

    AppRow GetRow(string path, string name, AppRule? rule = null)
    {
        if (_map.TryGetValue(path, out var row)) return row;
        row = new AppRow(path, name);
        if (rule != null)
        {
            row.DownLimitKb = rule.DownLimit / 1024; row.UpLimitKb = rule.UpLimit / 1024;
            row.Block = rule.Block; row.Priority = rule.Priority;
        }
        row.Edited = Apply;
        _map[path] = row;
        Rows.Add(row);
        return row;
    }

    void Apply()
    {
        if (_loading) return;
        _engine.SetRules(Rows.Where(r => !r.IsTotal && !r.IsDefault).Select(r => r.ToRule()));
        _engine.GlobalDownLimit = GlobalDownKb * 1024;
        _engine.GlobalUpLimit = GlobalUpKb * 1024;
        _engine.Paused = Paused;
        Save();
    }

    void Save()
    {
        if (_loading) return;
        var rules = Rows.Where(r => !r.IsTotal && !r.IsDefault).Select(r => r.ToRule()).ToList();
        try
        {
            RuleStore.Save(_file, new Settings(rules, GlobalDownKb * 1024, GlobalUpKb * 1024,
                Language, StartMinimized, Autostart, _engineEnabled, ShowChart, Profiles, ActiveProfile));
        }
        catch (System.IO.IOException) { } // Speichern ist nicht kritisch
    }

    partial void OnGlobalDownKbChanged(long value) { if (TotalRow.DownLimitKb != value) TotalRow.DownLimitKb = value; Apply(); }
    partial void OnGlobalUpKbChanged(long value) { if (TotalRow.UpLimitKb != value) TotalRow.UpLimitKb = value; Apply(); }
    partial void OnProcessesExpandedChanged(bool value) => View.Refresh();
    partial void OnPausedChanged(bool value) => Apply();
    partial void OnSearchChanged(string value) => View.Refresh();
    partial void OnSelectedChanged(AppRow? value)
    {
        if (value == null) { Selected = TotalRow; return; } // Auswahl nie leer: dann Gesamtansicht
        OnPropertyChanged(nameof(ChartTitle));
        PublishChart();
    }
    partial void OnStartMinimizedChanged(bool value) => Save();
    partial void OnShowChartChanged(bool value) => Save();
    partial void OnOnlyActiveChanged(bool value) => View.Refresh();
    partial void OnActiveProfileChanged(string? value) => Save();

    [RelayCommand]
    void ShowTotal() => Selected = TotalRow;

    [RelayCommand]
    void ToggleProcesses() => ProcessesExpanded = !ProcessesExpanded;
    partial void OnLanguageChanged(string value) { Loc.Instance.Set(value); Save(); }

    partial void OnAutostartChanged(bool value)
    {
        if (_loading || _syncingAutostart) return;
        if (!Platform.SetAutostart(value))
        {
            _syncingAutostart = true; Autostart = !value; _syncingAutostart = false; // zurücksetzen
            MessageBox.Show(T("msg_autostart_failed"), "TrafficLimiter");
            return;
        }
        Save();
    }

    [RelayCommand]
    void ToggleEngine()
    {
        if (_engine.Running)
        {
            _engine.Stop(); EngineOn = false; _engineEnabled = false;
        }
        else
        {
            _engineEnabled = true;           // Absicht merken, auch wenn der Start (z. B. ohne Admin) scheitert
            StartEngine(interactive: true);
        }
        Save();
    }

    bool StartEngine(bool interactive)
    {
        try
        {
            _engine.Start();
            _noAdmin = false; EngineOn = true;
            return true;
        }
        catch (Exception e) when (e is Win32Exception or DllNotFoundException)
        {
            EngineOn = false;
            _noAdmin = e is Win32Exception && !Platform.IsElevated;
            OnPropertyChanged(nameof(Status));
            if (!interactive) return false;   // beim Programmstart keine Dialoge
            if (e is DllNotFoundException) MessageBox.Show(T("msg_dll"), "TrafficLimiter");
            else if (_noAdmin)
            {
                if (MessageBox.Show(T("msg_admin"), "TrafficLimiter", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
                    Application.Current.Shutdown();
                }
            }
            else MessageBox.Show(e.Message, T("msg_fail"));
            return false;
        }
    }

    [RelayCommand]
    void Reset(AppRow? row)
    {
        if (row == null) return;
        row.DownLimitKb = 0; row.UpLimitKb = 0; row.Block = false; row.Priority = Priority.Normal;
    }

    /// <summary>Aktuelle Regeln + Gesamtlimits unter einem Namen speichern (gleicher Name überschreibt).</summary>
    public void SaveProfile(string name)
    {
        var p = new Profile(name, Rows.Where(r => !r.IsTotal && !r.IsDefault).Select(r => r.ToRule()).ToList(),
            GlobalDownKb * 1024, GlobalUpKb * 1024);
        int i = Profiles.FindIndex(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) Profiles[i] = p; else Profiles.Add(p);
        ActiveProfile = p.Name;
        Save();
    }

    public void ApplyProfile(Profile p)
    {
        _loading = true; // Sammelumschaltung: nur ein Engine-Update und ein Speichern am Ende
        try
        {
            foreach (var r in Rows.Where(r => !r.IsTotal))
            { r.DownLimitKb = 0; r.UpLimitKb = 0; r.Block = false; r.Priority = Priority.Normal; }
            foreach (var rule in p.Rules)
            {
                var row = GetRow(rule.AppPath, System.IO.Path.GetFileName(rule.AppPath));
                row.DownLimitKb = rule.DownLimit / 1024; row.UpLimitKb = rule.UpLimit / 1024;
                row.Block = rule.Block; row.Priority = rule.Priority;
            }
            GlobalDownKb = p.GlobalDownLimit / 1024; GlobalUpKb = p.GlobalUpLimit / 1024;
            ActiveProfile = p.Name;
        }
        finally { _loading = false; }
        Apply();
    }

    public void DeleteActiveProfile()
    {
        if (ActiveProfile == null) return;
        Profiles.RemoveAll(x => x.Name == ActiveProfile);
        ActiveProfile = null;
        Save();
    }

    public void Shutdown() => _engine.Dispose();

    void Tick()
    {
        if (!_engine.Running) return;
        double now = Clock.Now;
        long td = 0, tu = 0;
        foreach (var s in _engine.Snapshot())
        {
            var row = GetRow(s.Path, s.Name);
            row.DownRate = s.DownRate; row.UpRate = s.UpRate;
            row.TotalDown = s.TotalDown; row.TotalUp = s.TotalUp;
            td += s.DownRate; tu += s.UpRate;
            row.AddSample(new Sample(now, s.DownRate, s.UpRate));
        }
        TotalRow.DownRate = td; TotalRow.UpRate = tu;
        TotalRow.TotalDown = Rows.Where(r => !r.IsTotal).Sum(r => r.TotalDown);
        TotalRow.TotalUp = Rows.Where(r => !r.IsTotal).Sum(r => r.TotalUp);
        TotalRow.AddSample(new Sample(now, td, tu));

        DownText = Fmt.Bytes(td) + "/s";
        UpText = Fmt.Bytes(tu) + "/s";
        Totals = $"↓ {DownText}   ↑ {UpText}";
        PublishChart();
    }

    /// <summary>Verlauf der gewählten App, sonst des ganzen Rechners.</summary>
    void PublishChart() => ChartSamples = Selected?.History.ToArray() ?? [];
}
