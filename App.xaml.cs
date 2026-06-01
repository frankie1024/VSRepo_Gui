using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using VSRepo_Gui.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace VSRepo_Gui;

public partial class App : Application
{
    private readonly AppStateService _appStateService = new();
    private AppThemeMode _themeMode = AppThemeMode.System;

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            AppLog.Write(e.Exception, "DispatcherUnhandledException");
            e.Handled = true;
            System.Windows.MessageBox.Show(
                e.Exception.Message,
                "VSRepo_Gui",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error
            );
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                AppLog.Write(ex, "AppDomainUnhandledException");
            }
            else
            {
                AppLog.Write($"AppDomainUnhandledException: {e.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _themeMode = _appStateService.Load().ThemeMode;
        ApplyThemeMode(_themeMode);
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        base.OnExit(e);
    }

    public AppThemeMode ThemeMode => _themeMode;

    public ApplicationTheme GetEffectiveTheme()
    {
        return ResolveTheme(_themeMode);
    }

    public void SetThemeMode(AppThemeMode mode)
    {
        _themeMode = mode;
        ApplyThemeMode(mode);
    }

    public void ApplyThemeToWindow(Window window)
    {
        var theme = GetEffectiveTheme();
        _ = WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Mica);
        WindowBackgroundManager.UpdateBackground(window, theme, WindowBackdropType.Mica);
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_themeMode == AppThemeMode.System)
        {
            Dispatcher.InvokeAsync(() => ApplyThemeMode(_themeMode));
        }
    }

    private void ApplyThemeMode(AppThemeMode mode)
    {
        var theme = ResolveTheme(mode);
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, true);
        UpdateStatusColors(theme == ApplicationTheme.Dark);

        foreach (Window window in Windows)
        {
            _ = WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Mica);
            WindowBackgroundManager.UpdateBackground(window, theme, WindowBackdropType.Mica);
        }
    }

    private static void UpdateStatusColors(bool isDark)
    {
        if (isDark)
        {
            SetColor("StatUpdatesBrush", "#4CB4FF");
            SetColor("StatUpdatesBackgroundBrush", "#1A3A5C");
            SetColor("StatInstalledBrush", "#6CCB5F");
            SetColor("StatInstalledBackgroundBrush", "#1A3A1A");
            SetColor("StatPendingBrush", "#FDCF6E");
            SetColor("StatPendingBackgroundBrush", "#3D2E00");
            SetColor("StatUnknownBrush", "#FF6B6B");
            SetColor("StatUnknownBackgroundBrush", "#3D1A1A");
        }
        else
        {
            SetColor("StatUpdatesBrush", "#0F6CBD");
            SetColor("StatUpdatesBackgroundBrush", "#DCEBFA");
            SetColor("StatInstalledBrush", "#0E8A16");
            SetColor("StatInstalledBackgroundBrush", "#DFF6DD");
            SetColor("StatPendingBrush", "#9D5D00");
            SetColor("StatPendingBackgroundBrush", "#FFF4CE");
            SetColor("StatUnknownBrush", "#C42B1C");
            SetColor("StatUnknownBackgroundBrush", "#FDE7E9");
        }

        static void SetColor(string key, string color)
        {
            var newBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            newBrush.Freeze();
            Application.Current.Resources[key] = newBrush;
        }
    }

    private static ApplicationTheme ResolveTheme(AppThemeMode mode)
    {
        return mode switch
        {
            AppThemeMode.Light => ApplicationTheme.Light,
            AppThemeMode.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light,
        };
    }
}

