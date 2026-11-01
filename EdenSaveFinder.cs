using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MKWWinUI;

/// <summary>
/// Best-effort local Eden account.sav discovery. Only searches plausible Eden
/// emulator data folders and verifies the expected Eden save length.
/// No elevated permissions, no writes, no scanning the whole disk.
/// </summary>
public static class EdenSaveFinder
{
    private const long EdenSaveSize = 850_928;
    private const int MaxDirectories = 12000;
    private const int MaxDepth = 11;

    public static IReadOnlyList<string> Find()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static void Add(HashSet<string> set, string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                set.Add(Path.GetFullPath(path));
        }
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var parent in new[] { roaming, local })
        {
            foreach (var name in new[] { "eden", "Eden", "Eden Emulator", "eden-emu" })
                Add(roots, Path.Combine(parent, name));
        }
        // Portable installs often keep user data alongside the emulator.
        foreach (var parent in new[]
        {
            Path.Combine(user, "AppData", "Roaming"),
            Path.Combine(user, "AppData", "Local"),
            Path.Combine(user, "Documents"),
            Path.Combine(user, "Downloads"),
            Path.Combine(user, "Desktop")
        })
        {
            if (!Directory.Exists(parent)) continue;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(parent))
                {
                    var name = Path.GetFileName(dir);
                    if (name.Contains("eden", StringComparison.OrdinalIgnoreCase))
                        Add(roots, dir);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        var results = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string path, int depth)>();
        foreach (var root in roots) pending.Push((root, 0));
        while (pending.Count > 0 && visited.Count < MaxDirectories)
        {
            var (folder, depth) = pending.Pop();
            if (!visited.Add(folder)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "account.sav", SearchOption.TopDirectoryOnly))
                {
                    var info = new FileInfo(file);
                    if (info.Length == EdenSaveSize) results.Add(info.FullName);
                }
                if (depth >= MaxDepth) continue;
                foreach (var dir in Directory.EnumerateDirectories(folder))
                {
                    var attrs = File.GetAttributes(dir);
                    if ((attrs & FileAttributes.ReparsePoint) != 0) continue;
                    pending.Push((dir, depth + 1));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (System.Security.SecurityException) { }
        }
        return results.Distinct(StringComparer.OrdinalIgnoreCase)
                      .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                      .ToArray();
    }
}
