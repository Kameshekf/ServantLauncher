using System;
using System.IO;

namespace ServantInstaller
{
    public static class ShortcutHelper
    {
        public static void CreateShortcut(string shortcutFilePath, string targetExePath, string workingDirectory, string? iconPath = null, string description = "Servant Launcher")
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(shortcutFilePath);
                    shortcut.TargetPath = targetExePath;
                    shortcut.WorkingDirectory = workingDirectory;
                    shortcut.Description = description;
                    if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    {
                        shortcut.IconLocation = $"{iconPath},0";
                    }
                    shortcut.Save();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating shortcut at {shortcutFilePath}: {ex.Message}");
            }
        }
    }
}
