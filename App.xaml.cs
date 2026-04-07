using System.Windows;
using vsrepo_Gui.Services;

namespace vsrepo_Gui;

public partial class App : Application
{
    public App()
    {
        AppLog.Write("App constructor");

        DispatcherUnhandledException += (_, e) =>
        {
            AppLog.Write(e.Exception, "DispatcherUnhandledException");
            e.Handled = true;
            MessageBox.Show(e.Exception.ToString(), "vsrepo_Gui", MessageBoxButton.OK, MessageBoxImage.Error);
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

}
