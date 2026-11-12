using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace TrafficLimiter.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();
    readonly WinForms.NotifyIcon _tray = new();
    bool _exit;

    public bool StartHidden => _vm.StartMinimized;

    /// <summary>Version aus dem csproj, ohne das Git-Hash-Suffix (+abc123).</summary>
    public static string AppVersion { get; } = "v" + (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
        System.Reflection.Assembly.GetExecutingAssembly())?.InformationalVersion ?? "?").Split('+')[0];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Application.Current.SessionEnding += (_, _) => _exit = true; // Abmelden/Herunterfahren nicht blockieren

        using (var icon = Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"))!.Stream)
            _tray.Icon = new System.Drawing.Icon(icon, WinForms.SystemInformation.SmallIconSize);
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.ContextMenuStrip = new();
        _tray.ContextMenuStrip.Opening += (_, _) => BuildTrayMenu(); // Zustand (Pause, Profil, Engine) beim Öffnen aktuell
        BuildTrayMenu();
        Loc.Instance.Changed += BuildTrayMenu;
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.Totals)) _tray.Text = _vm.Totals; };
    }

    void BuildTrayMenu()
    {
        var m = _tray.ContextMenuStrip!.Items;
        m.Clear();
        m.Add(Loc.T("tray_open"), null, (_, _) => ShowWindow());
        m.Add(new WinForms.ToolStripSeparator());
        m.Add(new WinForms.ToolStripMenuItem(Loc.T("btn_pause"), null, (_, _) => _vm.Paused = !_vm.Paused) { Checked = _vm.Paused });
        m.Add(Loc.T(_vm.EngineOn ? "btn_stop" : "btn_start"), null, (_, _) => _vm.ToggleEngineCommand.Execute(null));
        var profiles = new WinForms.ToolStripMenuItem(Loc.T("tray_profiles")) { Enabled = _vm.Profiles.Count > 0 };
        foreach (var p in _vm.Profiles.ToList())
            profiles.DropDownItems.Add(new WinForms.ToolStripMenuItem(p.Name, null, (_, _) => _vm.ApplyProfile(p)) { Checked = p.Name == _vm.ActiveProfile });
        m.Add(profiles);
        m.Add(new WinForms.ToolStripSeparator());
        m.Add(Loc.T("tray_exit"), null, (_, _) => { _exit = true; Close(); });
    }

    void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }

    void OpenSettings(object sender, RoutedEventArgs e) => new SettingsWindow { Owner = this, DataContext = _vm }.ShowDialog();

    /// <summary>Profil-Menü: wechseln, aktuelle Einstellungen speichern, aktives Profil löschen.</summary>
    void OpenProfiles(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        foreach (var p in _vm.Profiles.ToList())
        {
            var item = new MenuItem { Header = p.Name, IsChecked = p.Name == _vm.ActiveProfile };
            item.Click += (_, _) => _vm.ApplyProfile(p);
            menu.Items.Add(item);
        }
        if (_vm.Profiles.Count > 0) menu.Items.Add(new Separator());

        var save = new MenuItem { Header = Loc.T("profile_save") };
        save.Click += (_, _) =>
        {
            var dlg = new NameDialog { Owner = this };
            if (dlg.ShowDialog() == true) _vm.SaveProfile(dlg.ProfileName);
        };
        menu.Items.Add(save);

        var del = new MenuItem { Header = Loc.T("profile_delete"), IsEnabled = _vm.ActiveProfile != null };
        del.Click += (_, _) => _vm.DeleteActiveProfile();
        menu.Items.Add(del);
        menu.IsOpen = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exit) { e.Cancel = true; Hide(); return; } // Schließen = in den Tray
        _tray.Dispose();
        _vm.Shutdown();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
