using System;
using System.Windows;

namespace TERMINAL
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                // ★ ПУТЬ К ПАПКЕ ПРОЕКТА (где лежит runtimes) ★
                // AppDomain.CurrentDomain.BaseDirectory — это bin\Debug\...
                // Нам нужно подняться на 3 уровня вверх до папки проекта
                string projectDir = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));

                string correctDllPath = System.IO.Path.Combine(
                    projectDir, "runtimes", "win", "lib", "net8.0", "System.IO.Ports.dll");

                if (System.IO.File.Exists(correctDllPath))
                {
                    System.Reflection.Assembly.LoadFrom(correctDllPath);
                }
                else
                {
                    // Если не нашли в проекте, пробуем в выходной папке (на случай, если копирование сработало)
                    string fallbackPath = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "runtimes", "win", "lib", "net8.0", "System.IO.Ports.dll");
                    if (System.IO.File.Exists(fallbackPath))
                        System.Reflection.Assembly.LoadFrom(fallbackPath);
                }
            }
            catch { }

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

        public App()
        {
            // ★ ПЕРЕХВАТ ЗАГРУЗКИ System.IO.Ports ★
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                if (args.Name.StartsWith("System.IO.Ports"))
                {
                    // Путь к правильной DLL (в папке runtimes проекта или в выходной папке)
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string correctPath = System.IO.Path.Combine(baseDir, "runtimes", "win", "lib", "net8.0", "System.IO.Ports.dll");

                    if (!System.IO.File.Exists(correctPath))
                    {
                        // Если нет в выходной папке, ищем в папке проекта
                        string projectDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", ".."));
                        correctPath = System.IO.Path.Combine(projectDir, "runtimes", "win", "lib", "net8.0", "System.IO.Ports.dll");
                    }

                    if (System.IO.File.Exists(correctPath))
                    {
                        return System.Reflection.Assembly.LoadFrom(correctPath);
                    }
                }
                return null; // Пусть ищет дальше
            };
        }
    }
}