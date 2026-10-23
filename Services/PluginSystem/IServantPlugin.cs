using System;
using ServantLauncher.Models;

namespace ServantLauncher.Services.PluginSystem
{
    public interface IPluginContext
    {
        IProfileManager? Profiles { get; }
        IGameLauncherService? Launcher { get; }
        IThemeService? Themes { get; }
        IAccountService? Accounts { get; }
        IGlobalProgressService? Progress { get; }

        void Log(string pluginId, string message);
        void RegisterPreLaunchHook(Action<GameProfile> hook);
        void RegisterPostLaunchHook(Action<GameProfile, int> hook);
    }

    public interface IServantPlugin
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }
        string Author { get; }
        string Description { get; }

        void OnEnable(IPluginContext context);
        void OnDisable();
    }
}
