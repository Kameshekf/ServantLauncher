using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;

namespace ServantInstaller
{
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                var log = Path.Combine(Path.GetTempPath(), "servant_setup_error.log");
                File.WriteAllText(log, ex.ToString());
                MessageBox(IntPtr.Zero, $"Ошибка запуска установщика:\n\n{ex.Message}\n\nПодробности в {log}", "Servant Launcher Setup", 0x10);
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
