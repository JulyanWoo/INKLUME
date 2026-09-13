using System.Windows.Media;

namespace Inklume.Desktop.Services;

public interface IPagePreviewLoader
{
    Task<ImageSource> LoadAsync(string filePath, CancellationToken cancellationToken);
}
