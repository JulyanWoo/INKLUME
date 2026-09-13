namespace Inklume.Application.Projects;

public sealed class ProjectOperationException : Exception
{
    public ProjectOperationException(ProjectErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ProjectOperationException(ProjectErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public ProjectErrorCode Code { get; }
}
