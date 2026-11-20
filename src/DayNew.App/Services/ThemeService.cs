using System.Windows;
using Microsoft.Win32;

namespace DayNew.Services;

public static class ThemeService
{
    private const string DarkUri = "pack://application:,,,/DayNew;component/Themes/Dark.xaml";
    private const string LightUri = "pack://application:,,,/DayNew;component/Themes/Light.xaml";

    public static bool IsDark { get; private set; } = true;

    public static void ApplyInitial() => Apply(ReadSystemPrefersDark());

    public static void Toggle() => Apply(!IsDark);

    public static bool ReadSystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // AppsUseLightTheme: 1 = 浅色, 0 = 深色
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return true;
        }
    }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var app = Application.Current;
        if (app is null) return;

        var dict = new ResourceDictionary
        {
            Source = new Uri(dark ? DarkUri : LightUri, UriKind.Absolute)
        };

        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0) merged[0] = dict;
        else merged.Add(dict);
    }
}
