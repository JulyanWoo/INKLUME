using Inklume.Domain.Projects;

namespace Inklume.Application.Projects;

public sealed record ProjectWorkspace(TranslationProject Project, string RootPath);
