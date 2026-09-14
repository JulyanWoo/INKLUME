using System.IO;

namespace Inklume.Desktop.ViewModels;

public static class PathSafety
{
    public const string DatabaseFileName = "project.db";
    public const string CreationLockFileName = ".inklume-create.lock";

    public static bool IsDescendantOf(string rootPath, string candidatePath)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));

        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReparsePoint(string path)
    {
        return Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }
}
