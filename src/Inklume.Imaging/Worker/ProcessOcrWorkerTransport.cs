using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Inklume.Imaging.Worker;

public sealed class ProcessOcrWorkerTransport(
    string pythonPath,
    string workerScriptPath,
    string? workingDirectory = null,
    IReadOnlyDictionary<string, string>? environmentVariables = null) : IOcrWorkerTransport
{
    private const int MaxStderrLines = 100;
    private readonly ConcurrentQueue<string> _stderrLines = new();
    private Process? _process;
    private StreamWriter? _stdinWriter;
    private StreamReader? _stdoutReader;
    private Task? _stderrReadingTask;
    private CancellationTokenSource? _processLifetimeCts;
    private bool _isDisposed;

    public bool IsRunning => _process is not null && !_process.HasExited;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(pythonPath))
        {
            throw new FileNotFoundException("The specified Python interpreter was not found.", pythonPath);
        }

        if (!File.Exists(workerScriptPath))
        {
            throw new FileNotFoundException("The OCR worker entry script was not found.", workerScriptPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("-u"); // Unbuffered binary stdout/stderr in Python
        startInfo.ArgumentList.Add(workerScriptPath);

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }
        else
        {
            startInfo.WorkingDirectory = Path.GetDirectoryName(workerScriptPath) ?? AppContext.BaseDirectory;
        }

        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        if (environmentVariables is not null)
        {
            foreach ((string key, string value) in environmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to launch the OCR worker process.");
        }

        _process = process;
        _stdinWriter = process.StandardInput;
        _stdoutReader = process.StandardOutput;
        _processLifetimeCts = new CancellationTokenSource();

        _stderrReadingTask = Task.Run(() => ReadStderrLoopAsync(process.StandardError, _processLifetimeCts.Token));

        return Task.CompletedTask;
    }

    public async Task SendLineAsync(string jsonLine, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!IsRunning || _stdinWriter is null)
        {
            throw new InvalidOperationException("OCR worker process is not running.");
        }

        await _stdinWriter.WriteLineAsync(jsonLine.AsMemory(), cancellationToken);
        await _stdinWriter.FlushAsync(cancellationToken);
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!IsRunning || _stdoutReader is null)
        {
            throw new InvalidOperationException("OCR worker process is not running.");
        }

        return await _stdoutReader.ReadLineAsync(cancellationToken);
    }

    public string GetRecentStderr()
        => string.Join(Environment.NewLine, _stderrLines.ToArray());

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        try
        {
            _processLifetimeCts?.Cancel();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(cancellationToken);
            }
        }
        catch
        {
            // Best-effort termination
        }
        finally
        {
            CleanupProcess();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await StopAsync(CancellationToken.None);
        _processLifetimeCts?.Dispose();
    }

    private async Task ReadStderrLoopAsync(StreamReader stderr, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await stderr.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                _stderrLines.Enqueue(line);
                while (_stderrLines.Count > MaxStderrLines)
                {
                    _stderrLines.TryDequeue(out _);
                }
            }
        }
        catch
        {
            // Stderr stream ended or cancelled
        }
    }

    private void CleanupProcess()
    {
        try
        {
            _stdinWriter?.Dispose();
            _stdoutReader?.Dispose();
            _process?.Dispose();
        }
        catch
        {
            // Ignore disposal exceptions
        }
        finally
        {
            _stdinWriter = null;
            _stdoutReader = null;
            _process = null;
        }
    }
}
