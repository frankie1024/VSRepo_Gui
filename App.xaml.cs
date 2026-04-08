using System.Windows;
using VSRepo_Gui.Services;

namespace VSRepo_Gui;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            AppLog.Write(e.Exception, "DispatcherUnhandledException");
            e.Handled = true;
            MessageBox.Show(e.Exception.Message, "VSRepo_Gui", MessageBoxButton.OK, MessageBoxImage.Error);
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

