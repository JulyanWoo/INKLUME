using Inklume.Application.Projects;

namespace Inklume.Infrastructure.Projects;

internal static class ProjectPaths
{
    internal const string DatabaseFileName = "project.db";
    internal const string CreationLockFileName = ".inklume-create.lock";

    internal static string Normalize(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
                || path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                throw new ArgumentException("Select an absolute folder path on a local drive.");
            }

            string pathRoot = Path.GetPathRoot(path) ?? string.Empty;
            string[] segments = path[pathRoot.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0 || segments.Any(IsInvalidSegment))
            {
                throw new ArgumentException("Use a folder path without traversal or invalid file names.");
            }

            string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            RejectReparsePoints(normalizedPath);
            return normalizedPath;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ProjectOperationException(ProjectErrorCode.InvalidPath, exception.Message, exception);
        }
    }

    internal static void RejectReparsePoints(string path)
    {
        string? current = path;
        while (current is not null)
        {
            if (Path.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ProjectOperationException(
                    ProjectErrorCode.InvalidPath, "Project paths must not pass through symbolic links or junctions.");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    internal static bool IsDescendantOf(string rootPath, string candidatePath)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));

        return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsReparsePoint(string path)
    {
        return Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    internal static void RequireDirectory(string path)
    {
        RejectReparsePoints(path);
        if (!Directory.Exists(path))
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidProject, $"The project is missing a required folder: {Path.GetFileName(path)}.");
        }
    }

    internal static void RequireFile(string path)
    {
        RejectReparsePoints(path);
        if (!File.Exists(path))
        {
            throw new ProjectOperationException(
                ProjectErrorCode.InvalidProject, $"The project is missing a required file: {Path.GetFileName(path)}.");
        }
    }

    private static bool IsInvalidSegment(string segment)
    {
        if (segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.')
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return true;
        }

        string deviceName = segment.Split('.')[0].ToUpperInvariant();
        return deviceName is "CON" or "PRN" or "AUX" or "NUL"
            || (deviceName.Length == 4 && deviceName[3] is >= '1' and <= '9'
                && (deviceName.StartsWith("COM", StringComparison.Ordinal)
                    || deviceName.StartsWith("LPT", StringComparison.Ordinal)));
    }
}
