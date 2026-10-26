using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace GitlabCICDNotify.App;

public enum AppTheme
{
    System = 0,
    Dark = 1,
    Light = 2
}

public sealed class ThemeService(
    string settingsPath = @"Software\GitlabCICDNotify",
    Func<bool>? systemIsDark = null) : IDisposable
{
    private static readonly Dictionary<string, SolidColorBrush> StateBrushes = [];
    private bool _subscribed;
    private bool _disposed;
    public AppTheme Preference { get; private set; } = AppTheme.System;

    public AppTheme ReadPreference()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(settingsPath);
        return settings?.GetValue("Theme") is int value && Enum.IsDefined(typeof(AppTheme), value)
            ? (AppTheme)value : AppTheme.System;
    }

    public void Initialize()
    {
        Preference = ReadPreference();
        Refresh();
        if (_subscribed) return;
        SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
        _subscribed = true;
    }

    public void SetPreference(AppTheme preference)
    {
        if (!Enum.IsDefined(preference)) throw new ArgumentOutOfRangeException(nameof(preference));
        Application.Current.Dispatcher.VerifyAccess();
        using var settings = Registry.CurrentUser.CreateSubKey(settingsPath);
        // Save before applying, so a failed write leaves the current UI and preference intact.
        settings.SetValue("Theme", (int)preference, RegistryValueKind.DWord);
        Preference = preference;
        Refresh();
    }

    private static bool ReadSystemIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed) Refresh();
        }));
    }

    public void Refresh()
    {
        var app = Application.Current;
        app.Dispatcher.VerifyAccess();
        var dark = Preference == AppTheme.Dark || (Preference == AppTheme.System && (systemIsDark ?? ReadSystemIsDark)());
        // WPF's Fluent theme supplies native control templates and the title bar.
#pragma warning disable WPF0001
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        SetBrush("CanvasBrush", dark ? "#151B26" : "#F4F6FA", SystemColors.WindowColor);
        SetBrush("SurfaceBrush", dark ? "#202938" : "#FFFFFF", SystemColors.WindowColor);
        SetBrush("InkBrush", dark ? "#E6EDF7" : "#17243B", SystemColors.WindowTextColor);
        SetBrush("MutedBrush", dark ? "#A7B5CA" : "#65738A", SystemColors.WindowTextColor);
        SetBrush("AccentBrush", dark ? "#B4A8FF" : "#6251D8", SystemColors.HighlightColor);
        SetBrush("AccentTextBrush", dark ? "#17122E" : "#FFFFFF", SystemColors.HighlightTextColor);
        SetBrush("BorderBrush", dark ? "#3C4960" : "#DDE2EC", SystemColors.WindowTextColor);
        SetBrush("HoverBrush", dark ? "#37435C" : "#EBE8FC", SystemColors.HighlightColor);
        SetBrush("SuccessBrush", dark ? "#74D6A2" : "#2E7D52", SystemColors.WindowTextColor);
        SetBrush("ErrorBrush", dark ? "#FF9B9B" : "#B53C3C", SystemColors.WindowTextColor);
        SetBrush("ProgressBrush", dark ? "#B4A8FF" : "#6A5ACD", SystemColors.WindowTextColor);
    }

    private static void SetBrush(string key, string color, Color contrast)
    {
        var resources = Application.Current.Resources;
        var value = SystemParameters.HighContrast ? contrast : (Color)ColorConverter.ConvertFromString(color);
        if (StateBrushes.TryGetValue(key, out var stateBrush)) stateBrush.Color = value;
        if (resources[key] is SolidColorBrush { IsFrozen: false } brush) brush.Color = value;
        else resources[key] = new SolidColorBrush(value);
    }

    // Resource brushes may be frozen by styles. Bound dashboard rows keep a separate
    // mutable brush so changing the palette also updates already displayed states.
    public static Brush Brush(string key)
    {
        if (Application.Current?.TryFindResource(key) is not SolidColorBrush resource) return Brushes.SlateGray;
        if (!StateBrushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(resource.Color);
            StateBrushes.Add(key, brush);
        }
        return brush;
    }

    public void Dispose()
    {
        _disposed = true;
        if (_subscribed) SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
        _subscribed = false;
    }
}
