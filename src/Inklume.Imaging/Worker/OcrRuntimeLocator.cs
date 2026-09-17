namespace Inklume.Imaging.Worker;

public static class OcrRuntimeLocator
{
    public static string? FindPythonPath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        string? envPath = Environment.GetEnvironmentVariable("INKLUME_PYTHON_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        // Project-local virtualenv under .local/ocr/.venv/Scripts/python.exe
        string baseDir = AppContext.BaseDirectory;
        string current = baseDir;
        for (int i = 0; i < 6; i++)
        {
            string localVenvPython = Path.Combine(current, ".local", "ocr", ".venv", "Scripts", "python.exe");
            if (File.Exists(localVenvPython))
            {
                return Path.GetFullPath(localVenvPython);
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        // Standard user Python 3.11 / 3.12 installations
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] standardPaths =
        [
            Path.Combine(localAppData, "Programs", "Python", "Python311", "python.exe"),
            Path.Combine(localAppData, "Programs", "Python", "Python312", "python.exe")
        ];

        foreach (string candidate in standardPaths)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? FindWorkerScriptPath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        string baseDir = AppContext.BaseDirectory;
        string current = baseDir;
        for (int i = 0; i < 6; i++)
        {
            string scriptCandidate = Path.Combine(current, "tools", "ocr", "worker", "main.py");
            if (File.Exists(scriptCandidate))
            {
                return Path.GetFullPath(scriptCandidate);
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        return null;
    }

    public static bool IsRuntimeConfigured(string? explicitPython = null, string? explicitScript = null)
        => FindPythonPath(explicitPython) is not null && FindWorkerScriptPath(explicitScript) is not null;
}
