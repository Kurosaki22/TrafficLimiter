using System.Windows;

namespace TrafficLimiter.App;

/// <summary>Small name input (save profile).</summary>
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
