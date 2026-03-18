using System.IO;

namespace OpenKikaiSan.App.Utils;

public static class AppPaths
{
    public static string Root
    {
        get
        {
            var basePath = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );
            return Path.Combine(basePath, "OpenKikaiSan");
        }
    }

    public static string SettingsPath => Path.Combine(Root, "settings.json");
    public static string LogPath => Path.Combine(Root, "logs", "app.log");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
    }
}
