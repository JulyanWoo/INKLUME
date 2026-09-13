namespace Inklume.Infrastructure.Tests;

internal sealed class TemporaryWorkspace : IDisposable
{
    private readonly string _temporaryParent;

    public TemporaryWorkspace()
    {
        _temporaryParent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-workspaces"));
        RootPath = Path.Combine(_temporaryParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string GetPath(string name) => Path.Combine(RootPath, name);

    public void Dispose()
    {
        string fullPath = Path.GetFullPath(RootPath);
        string allowedPrefix = string.Concat(_temporaryParent, Path.DirectorySeparatorChar);
        if (!fullPath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase)
            || Path.GetDirectoryName(fullPath) != _temporaryParent)
        {
            throw new InvalidOperationException("The temporary workspace is outside its owned test directory.");
        }

        if (!Directory.Exists(fullPath))
        {
            return;
        }

        VerifyNoReparsePoints(new DirectoryInfo(fullPath));
        Directory.Delete(fullPath, recursive: true);
    }

    private static void VerifyNoReparsePoints(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("The temporary workspace contains a reparse point and cannot be removed recursively.");
        }

        if (entry is DirectoryInfo directory)
        {
            foreach (FileSystemInfo child in directory.EnumerateFileSystemInfos())
            {
                VerifyNoReparsePoints(child);
            }
        }
    }
}
