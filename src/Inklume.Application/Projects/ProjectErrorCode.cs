namespace Inklume.Application.Projects;

public enum ProjectErrorCode
{
    InvalidPath,
    NotFound,
    AlreadyExists,
    DirectoryNotEmpty,
    InvalidProject,
    IncompatibleVersion,
    AccessDenied,
    StorageFailure
}
