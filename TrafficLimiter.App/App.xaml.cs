using System.Windows;

namespace TrafficLimiter.App;

public partial class App : Application
{
    ResourceDictionary? _theme;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Enter in einem Textfeld übernimmt den Wert sofort (statt erst beim Verlassen des Felds)
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.TextBox), System.Windows.UIElement.KeyDownEvent,
            new System.Windows.Input.KeyEventHandler((s, k) =>
            {
                if (k.Key == System.Windows.Input.Key.Enter && s is System.Windows.Controls.TextBox tb)
                    tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            }));
        ShutdownMode = ShutdownMode.OnExplicitShutdown; // Fenster darf im Tray versteckt bleiben
        ApplyTheme();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.Invoke(ApplyTheme);

        var w = new MainWindow();
        MainWindow = w;
        if (!w.StartHidden) w.Show();
    }

    /// <summary>Eigene Farben passend zum Windows-Theme (Hell/Dunkel); das Fluent-Theme folgt dem System selbst.</summary>
    void ApplyTheme()
    {
        bool light = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is 1;
        if (_theme != null) Resources.MergedDictionaries.Remove(_theme);
        _theme = new ResourceDictionary { Source = new Uri(light ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative) };
        Resources.MergedDictionaries.Add(_theme);
    }
}
