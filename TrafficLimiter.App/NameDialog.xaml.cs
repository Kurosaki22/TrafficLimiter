using System.Windows;

namespace TrafficLimiter.App;

/// <summary>Kleine Namenseingabe (Profil speichern).</summary>
public partial class NameDialog : Window
{
    public string ProfileName => NameBox.Text.Trim();

    public NameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    void Ok(object sender, RoutedEventArgs e)
    {
        if (ProfileName.Length > 0) DialogResult = true;
    }
}
