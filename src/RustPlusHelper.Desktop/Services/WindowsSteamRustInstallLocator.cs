using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RustPlusHelper.Desktop.Services;

public static class WindowsSteamRustInstallLocator
{
    private static readonly Regex LibraryPathPattern = new(
        "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> FindInstallations()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(
            steamRoots,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam"));

        try
        {
            using var steamKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            AddIfPresent(steamRoots, steamKey?.GetValue("SteamPath") as string);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The standard Program Files location remains available as a fallback.
        }

        var libraryRoots = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in steamRoots)
        {
            foreach (var libraryPath in ReadLibraryPaths(
                Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf")))
            {
                AddIfPresent(libraryRoots, libraryPath);
            }
        }

        return libraryRoots
            .Select(root => Path.Combine(root, "steamapps", "common", "Rust"))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static IReadOnlyList<string> ReadLibraryPaths(string vdfPath)
    {
        try
        {
            if (!File.Exists(vdfPath))
            {
                return [];
            }

            var text = File.ReadAllText(vdfPath);
            return LibraryPathPattern.Matches(text)
                .Select(match => match.Groups["path"].Value.Replace("\\\\", "\\"))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void AddIfPresent(ISet<string> paths, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            paths.Add(Path.GetFullPath(path));
        }
    }
}
