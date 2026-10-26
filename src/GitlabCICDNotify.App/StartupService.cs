using System;
using System.IO;
using Microsoft.Win32;

namespace GitlabCICDNotify.App;

public sealed class StartupService(
    string settingsPath = @"Software\GitlabCICDNotify",
    string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
    string? executablePath = null)
{
    private const string RunValueName = "GitlabCICDNotify";
    private const string PreferenceName = "StartWithWindows";

    public bool IsEnabled
    {
        get
        {
            using var settings = Registry.CurrentUser.OpenSubKey(settingsPath);
            return settings?.GetValue(PreferenceName) is not int value || value != 0;
        }
    }

    public void Synchronize() => SetRegistration(IsEnabled);

    public void SetEnabled(bool enabled)
    {
        var previous = IsEnabled;
        SetRegistration(enabled);
        try
        {
            using var settings = Registry.CurrentUser.CreateSubKey(settingsPath);
            settings.SetValue(PreferenceName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch
        {
            SetRegistration(previous);
            throw;
        }
    }

    private void SetRegistration(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(runPath);
        if (enabled)
        {
            var path = executablePath ?? Path.Combine(AppContext.BaseDirectory, "GitlabCICDNotify.exe");
            if (!File.Exists(path))
                throw new InvalidOperationException("Automatické spúšťanie nemožno nastaviť: spustiteľný súbor aplikácie neexistuje.");
            run.SetValue(RunValueName, $"\"{Path.GetFullPath(path)}\"", RegistryValueKind.String);
        }
        else run.DeleteValue(RunValueName, throwOnMissingValue: false);
    }
}
