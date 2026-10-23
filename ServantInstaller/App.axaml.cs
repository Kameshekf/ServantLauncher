using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ServantInstaller
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "installer_debug.txt");
            try
            {
                File.AppendAllText(logPath, "App.Initialize entering\n");
                AvaloniaXamlLoader.Load(this);
                File.AppendAllText(logPath, "App.Initialize loaded XAML successfully\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, "App.Initialize EXCEPTION: " + ex + "\n");
                throw;
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "installer_debug.txt");
            try
            {
                File.AppendAllText(logPath, "OnFrameworkInitializationCompleted entering\n");
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var vm = new InstallerViewModel();
                    File.AppendAllText(logPath, "InstallerViewModel created\n");
                    var window = new MainWindow(vm);
                    File.AppendAllText(logPath, "MainWindow created\n");
                    desktop.MainWindow = window;
                    File.AppendAllText(logPath, "desktop.MainWindow assigned\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, "App Framework INIT EXCEPTION: " + ex + "\n");
                throw;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
