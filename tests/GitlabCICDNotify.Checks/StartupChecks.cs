using System.IO;
using System.Windows;
using System.Windows.Controls;
using GitlabCICDNotify.App;
using Microsoft.Win32;

namespace GitlabCICDNotify.Checks;

internal static partial class Program
{
    private static void StartupChecks()
    {
        var keyPath = @"Software\GitlabCICDNotify.Checks\" + Guid.NewGuid();
        var folder = Path.Combine(Path.GetTempPath(), "Gitlab startup " + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        var executable = Path.Combine(folder, "GitlabCICDNotify.exe");
        File.WriteAllText(executable, "Not executed: startup registration fixture");
        try
        {
            var startup = new StartupService(keyPath + @"\Settings", keyPath + @"\Run", executable);
            Check(startup.IsEnabled, "Startup is opt-out for missing preferences");
            startup.Synchronize();
            using (var run = Registry.CurrentUser.OpenSubKey(keyPath + @"\Run", writable: true)!)
            {
                Check((string?)run.GetValue("GitlabCICDNotify") == $"\"{executable}\"", "Startup quotes executable paths containing spaces without diagnostic arguments");
                run.SetValue("OtherApplication", "preserve");
            }
            startup.SetEnabled(false);
            var restarted = new StartupService(keyPath + @"\Settings", keyPath + @"\Run", executable);
            restarted.Synchronize();
            using (var run = Registry.CurrentUser.OpenSubKey(keyPath + @"\Run")!)
            {
                Check(!restarted.IsEnabled && run.GetValue("GitlabCICDNotify") == null, "Opt-out survives restart and removes only own startup registration");
                Check((string?)run.GetValue("OtherApplication") == "preserve", "Other startup entries are preserved");
            }
            restarted.SetEnabled(true);
            using (var run = Registry.CurrentUser.OpenSubKey(keyPath + @"\Run", writable: true)!)
                run.SetValue("GitlabCICDNotify", "old installation path");
            restarted.Synchronize();
            using (var run = Registry.CurrentUser.OpenSubKey(keyPath + @"\Run")!)
                Check(restarted.IsEnabled && (string?)run.GetValue("GitlabCICDNotify") == $"\"{executable}\"", "Enabled startup repairs an old installation path");
            restarted.SetEnabled(false);
            File.Delete(executable);
            try { restarted.SetEnabled(true); throw new Exception("Expected missing executable failure"); }
            catch (InvalidOperationException)
            {
                using var run = Registry.CurrentUser.OpenSubKey(keyPath + @"\Run")!;
                Check(!restarted.IsEnabled && run.GetValue("GitlabCICDNotify") == null, "Failed registration preserves saved opt-out");
            }
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            if (File.Exists(executable)) File.Delete(executable);
            Directory.Delete(folder);
        }
    }

    private static void RenderStartupSettings(App.App app, string folder)
    {
        var keyPath = @"Software\GitlabCICDNotify.Checks\" + Guid.NewGuid();
        try
        {
            var startup = new StartupService(keyPath + @"\Settings", keyPath + @"\Run", Environment.ProcessPath!);
            var window = new SettingsWindow(app, startup);
            Render(new SettingsWindow(app, startup), Path.Combine(folder, "settings.png"));
            window.Show();
            var checkbox = (CheckBox)window.FindName("StartWithWindows");
            Check(checkbox.IsChecked == true, "Settings show startup enabled by default");
            checkbox.IsChecked = false;
            checkbox.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(!startup.IsEnabled, "Settings checkbox immediately saves opt-out");
            window.Hide();
            window.Show();
            Check(checkbox.IsChecked == false, "Settings retain opt-out when reopened");
            checkbox.IsChecked = true;
            checkbox.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(startup.IsEnabled, "Settings checkbox can re-enable startup");
            window.Close();
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false); }
    }
}
