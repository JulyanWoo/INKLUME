namespace Inklume.Desktop.ViewModels;

public sealed class DialogCloseRequestedEventArgs(bool accepted) : EventArgs
{
    public bool Accepted { get; } = accepted;
}
