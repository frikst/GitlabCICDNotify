using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace GitlabCICDNotify.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var icon = BitmapDecoder.Create(new Uri("pack://application:,,,/GitlabCICDNotify;component/Assets/App.ico"),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        AppImage.Source = icon.Frames.MaxBy(frame => frame.PixelWidth);
        VersionText.Text = $"Verzia {typeof(AboutWindow).Assembly.GetName().Version?.ToString(3)}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Department_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { NotificationService.Open(e.Uri.AbsoluteUri); }
        catch (Exception)
        {
            MessageBox.Show(this, "Web katedry sa nepodarilo otvoriť. Skontrolujte predvolený prehliadač.",
                "O aplikácii", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
