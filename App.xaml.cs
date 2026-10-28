using System;
using System.Windows;

namespace TERMINAL
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Перехват необработанных исключений
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            base.OnStartup(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogFatalError(e.Exception);
            MessageBox.Show("Произошла критическая ошибка. Подробности в файле crash.log", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // Программа не закроется
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogFatalError(e.ExceptionObject as Exception);
        }

        private void LogFatalError(Exception ex)
        {
            if (ex == null) return;
            string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            string logText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL ERROR:\n{ex.Message}\n{ex.StackTrace}\n\n";
            System.IO.File.AppendAllText(logPath, logText);
        }
    }
}