namespace Jellyfin.Plugin.Chrono.Tests;

internal static class TestPaths
{
    public static string RegistryRoot { get; } = FindRegistry();

    private static string FindRegistry()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "registry", "index.json");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("registry/index.json not found");
    }
}
