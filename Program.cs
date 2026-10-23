using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ServantLauncher.Models;
using ServantLauncher.Services;

namespace ServantLauncher
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                try
                {
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash-launcher.log");
                    File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] UnhandledException: {e.ExceptionObject}\n");
                }
                catch { }
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                try
                {
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash-launcher.log");
                    File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] UnobservedTaskException: {e.Exception}\n");
                }
                catch { }
                e.SetObserved();
            };

            if (args.Length > 0 && args[0] == "--test-launch")
            {
                RunTestLaunchAsync().GetAwaiter().GetResult();
                return;
            }
            if (args.Length > 0 && args[0] == "--test-settings")
            {
                RunTestSettings();
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        private static async Task RunTestLaunchAsync()
        {
            Console.WriteLine("=== SERVANT LAUNCHER TEST RUNNER ===");
            var http = HttpHelper.CreateHttpClient();
            var mojang = new MojangService(http);
            var fabric = new FabricService(http);
            var java = new JavaService();
            var profiles = new ProfileManager();
            var launcher = new GameLauncherService(mojang, fabric, java, profiles);

            var testAccount = new AccountModel
            {
                Username = "TestPlayer",
                Type = AccountType.Offline,
                Uuid = Guid.NewGuid().ToString("N")
            };

            var testProfile = new GameProfile
            {
                Name = "Test Vanilla 1.21.4",
                GameVersion = "1.21.4",
                LoaderType = LoaderType.Vanilla,
                AllocatedRamMb = 3072
            };

            launcher.GameLogReceived += (s, e) =>
            {
                if (e.IsError)
                    Console.ForegroundColor = ConsoleColor.Red;
                else
                    Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(e.Text);
                Console.ResetColor();
            };

            launcher.GameExited += (s, code) =>
            {
                Console.WriteLine($"[TEST RUNNER] Game process exited with code: {code}");
            };

            var progress = new Progress<(double percent, string status)>(p =>
            {
                Console.WriteLine($"[{p.percent:F0}%] {p.status}");
            });

            Console.WriteLine("Starting Game Launch Test for 1.21.4 Fabric...");
            try
            {
                await launcher.LaunchGameAsync(testProfile, testAccount, progress);
                Console.WriteLine("Game launched! Waiting 20 seconds to observe logs...");
                await Task.Delay(20000);
                if (launcher.IsGameRunning)
                {
                    Console.WriteLine("Game is running successfully! Terminating test process...");
                    launcher.KillGame();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Launch error: {ex}");
                Console.ResetColor();
            }
        }

        private static void RunTestSettings()
        {
            try
            {
                var app = BuildAvaloniaApp();
                app.SetupWithoutStarting();
                var pm = new ProfileManager();
                var java = new JavaService();
                var rpc = new DiscordRpcService();
                var vm = new ServantLauncher.ViewModels.SettingsViewModel(pm, java, rpc);
                var view = new ServantLauncher.Views.SettingsView { DataContext = vm };
                Console.WriteLine("SUCCESS_SETTINGS_VIEW_CREATED");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("SETTINGS_VIEW_EXCEPTION: " + ex);
                Console.ResetColor();
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
