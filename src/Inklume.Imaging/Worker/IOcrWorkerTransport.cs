namespace Inklume.Imaging.Worker;

public interface IOcrWorkerTransport : IAsyncDisposable
{
    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task SendLineAsync(string jsonLine, CancellationToken cancellationToken);

    Task<string?> ReadLineAsync(CancellationToken cancellationToken);

    string GetRecentStderr();

    Task StopAsync(CancellationToken cancellationToken);
}
