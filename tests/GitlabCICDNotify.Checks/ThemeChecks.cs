using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GitlabCICDNotify.App;
using Microsoft.Win32;

namespace GitlabCICDNotify.Checks;

internal static partial class Program
{
    private static void ThemePreferenceChecks()
    {
        var path = @"Software\GitlabCICDNotify.Checks\" + Guid.NewGuid();
        try
        {
            using var themes = new ThemeService(path);
            Check(themes.ReadPreference() == AppTheme.System, "Missing theme preference follows Windows");
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("Theme", 99, RegistryValueKind.DWord);
            Check(themes.ReadPreference() == AppTheme.System, "Unknown saved theme falls back to system");
            key.SetValue("Theme", "Dark", RegistryValueKind.String);
            Check(themes.ReadPreference() == AppTheme.System, "Wrong registry value type falls back to system");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    private static void RenderThemes(App.App app, string folder, bool desktop)
    {
        var path = @"Software\GitlabCICDNotify.Checks\" + Guid.NewGuid();
        var systemDark = false;
        using var themes = new ThemeService(path, () => systemDark);
        try
        {
            themes.Initialize();
            var light = ((SolidColorBrush)app.Resources["CanvasBrush"]).Color;
            systemDark = true;
            themes.Refresh();
            var dark = ((SolidColorBrush)app.Resources["CanvasBrush"]).Color;
            Check(light != dark, "System theme responds to Windows app mode changes");
            themes.SetPreference(AppTheme.Light);
            themes.Refresh();
            Check(((SolidColorBrush)app.Resources["CanvasBrush"]).Color == light, "Explicit light ignores dark Windows app mode");

            var startup = new StartupService(path + @"\Settings", path + @"\Run", Environment.ProcessPath!);
            var settings = new SettingsWindow(app, startup, themes);
            var status = new StatusWindow(app);
            settings.Show();
            status.Show();
            var choice = (ComboBox)settings.FindName("ThemeChoice");
            Check((AppTheme)choice.SelectedValue == AppTheme.Light, "Settings show the saved theme");
            var success = ThemeService.Brush("SuccessBrush");
            choice.SelectedValue = AppTheme.Dark;
            PumpDispatcher();
            Check(themes.Preference == AppTheme.Dark && themes.ReadPreference() == AppTheme.Dark, "Theme selector immediately applies and persists dark mode");
            Check(((SolidColorBrush)status.Background).Color == dark, "An existing status window switches theme immediately");
            Check(ReferenceEquals(success, ThemeService.Brush("SuccessBrush")), "Existing bound state brushes remain live across theme switches");
            using (var restarted = new ThemeService(path))
                Check(restarted.ReadPreference() == AppTheme.Dark, "Saved theme survives service restart");
            settings.Hide();
            systemDark = false;
            themes.Refresh();
            Check(((SolidColorBrush)status.Background).Color == dark, "Explicit dark ignores light Windows app mode while settings are hidden");
            settings.Show();
            Check((AppTheme)choice.SelectedValue == AppTheme.Dark, "Reopened settings retain the selected theme");
            choice.IsDropDownOpen = true;
            PumpDispatcher();
            Check(choice.IsDropDownOpen, "Theme dropdown remains usable in dark mode");
            choice.IsDropDownOpen = false;
            Render(settings, Path.Combine(folder, "settings-dark.png"));
            Render(status, Path.Combine(folder, "status-dark.png"));
            Render(new ConnectAccountWindow(app), Path.Combine(folder, "connect-account-dark.png"));
            Render(new AboutWindow(), Path.Combine(folder, "about-dark.png"));
            var darkFolder = Path.Combine(folder, "dark");
            Directory.CreateDirectory(darkFolder);
            RenderProgress(darkFolder);
            if (desktop) NativeMenuChecks.Run(Check);

            themes.SetPreference(AppTheme.Light);
            Render(new SettingsWindow(app, startup, themes), Path.Combine(folder, "settings-light.png"));
            if (desktop) NativeMenuChecks.Run(Check);
            themes.SetPreference(AppTheme.System);
            Check(themes.ReadPreference() == AppTheme.System, "System choice is saved explicitly");
            systemDark = true;
            themes.Refresh();
            Check(((SolidColorBrush)app.Resources["CanvasBrush"]).Color == dark, "Returning to system resumes following Windows");
            try { themes.SetPreference((AppTheme)99); throw new Exception("Expected invalid theme rejection"); }
            catch (ArgumentOutOfRangeException)
            {
                Check(themes.ReadPreference() == AppTheme.System && themes.Preference == AppTheme.System, "Invalid theme does not overwrite the saved choice");
            }
            themes.SetPreference(AppTheme.Light);
            using var failingTheme = new ThemeService(new string('x', 256));
            var failedWindow = new SettingsWindow(app, startup, failingTheme);
            failedWindow.Show();
            var failedChoice = (ComboBox)failedWindow.FindName("ThemeChoice");
            failedChoice.SelectedValue = AppTheme.Dark;
            Check(failingTheme.Preference == AppTheme.System && (AppTheme)failedChoice.SelectedValue == AppTheme.System,
                "Failed theme write restores the selector and leaves the current preference intact");
            Check(((SolidColorBrush)app.Resources["CanvasBrush"]).Color == light, "Failed theme write leaves the active palette intact");
            failedWindow.Close();
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }
}

