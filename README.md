# INKLUME

AI Comic Localization Studio for Windows, intended for assisted localization
of manhwa, manga, webtoons, and comics.

This early version creates and reopens local localization projects, preserving
their identity, names, and timestamps. Comic processing is not implemented yet.

## Stack and requirements

- Windows x64 and .NET SDK 10.0.301 (compatible patches in the 10.0.3xx band).
- C#, .NET 10, WPF, and CommunityToolkit.Mvvm.
- Modular monolith; xUnit.net v3 tests.
- SQLite with Entity Framework Core 10 for project metadata.
- Access to NuGet.org for the initial dependency restore.

Read the external rules referenced in `AGENTS.md` before development.

## Development

Run these commands from the repository root in a dedicated PowerShell session:

```powershell
. .\scripts\Enter-Development.ps1
dotnet --version
dotnet restore Inklume.sln
dotnet tool restore
dotnet build Inklume.sln --no-restore
dotnet test Inklume.sln --no-build --no-restore
dotnet run --project src/Inklume.Desktop/Inklume.Desktop.csproj --no-build --no-restore
```

The setup script redirects .NET/NuGet caches, application-data directories,
and temporary files to the ignored `.local/` directory for this shell and its
child processes only. It disables tool telemetry and persistent build servers.
It does not install tools or change Windows settings or PATH. Close the dedicated
shell when finished; start an IDE from that shell if the same isolation is needed.

Package versions are centralized in `Directory.Packages.props` so the four test
projects stay aligned. `NuGet.Config` selects NuGet.org and a local package cache.
Tests use `xunit.v3.mtp-off` with VSTest, avoiding the additional testing platform
and its telemetry extensions.

## Solution layout

```text
Inklume.sln
src/
    Inklume.Desktop/         WPF application and composition root
    Inklume.Application/     Application layer
    Inklume.Domain/          Domain layer
    Inklume.Infrastructure/  SQLite persistence and project filesystem
    Inklume.Imaging/         Future imaging implementations
    Inklume.AI/              Future AI integrations
    Inklume.Worker/          Future background orchestration library
tests/
    Inklume.Domain.Tests/
    Inklume.Application.Tests/
    Inklume.Infrastructure.Tests/
    Inklume.Imaging.Tests/
scripts/
    Enter-Development.ps1
```

Desktop composes Application and Infrastructure. Application depends on Domain;
Infrastructure implements Application's project store using Domain models.
Worker retains its Application reference but has no implementation. Other unused
modules remain independent. The small composition root uses constructor injection
without a DI container. EF Core and serialization stay inside Infrastructure.

Tests cover domain validation, application operations, layer boundaries, and
SQLite/filesystem round trips, including invalid data and preservation of existing
files. Tests own and clean unique directories under their ignored build output.
Imaging.Tests remains configured without cases until imaging is implemented.

## Create and open a project

Enter a project name, series name, and absolute folder path, then select
**Create project**. **Browse** selects an existing empty folder; a new folder may
also be entered if its parent already exists. Files are written directly inside
the selected folder. Names are metadata, not file paths.

Select **Open project** and choose the same folder to recover its information,
including after restarting INKLUME. The current project is shown below the form.
Closing during an operation requests cancellation and waits for it to finish.

```text
<selected folder>/
    project.db
    context/
        series.json
        characters.json
        glossary.json
        translation_rules.json
    chapters/
    cache/
```

The only application table is `Projects`: `Id`, `Name`, `SeriesName`, `CreatedAt`,
`UpdatedAt`, and `FormatVersion`. EF Core also maintains migration history and its
migration lock table. The folder path is derived when opening and is not stored
in the database, so a complete project folder can be moved while INKLUME is closed.

All four JSON files are UTF-8 documents with `formatVersion: 1` and `projectId`.
`series.json` additionally contains `name`. The other documents contain only those
headers; character, glossary, and translation-rule features are not implemented.

Opening uses a read-only database connection and validates the SQLite application
identifier, migration history, metadata, required directories, and JSON identity
and format. A folder containing an arbitrary `project.db` is not accepted.
Supported paths are local absolute paths without traversal, junctions, or symbolic
links. Context files are limited to 1 MiB each in this initial format.

Existing nonempty folders are never overwritten. Interrupted or failed creation
preserves partial files; it does not silently delete or recreate a database. Such
incomplete folders are rejected on opening. Select another empty folder to retry.

## Migrations

`dotnet-ef` is pinned in the local tool manifest. `InitialProject` creates the
schema and SQLite application identifier. Creation applies migrations to a new
database and commits metadata after the context files are ready. Opening currently
accepts only the supported format and migration history; incompatible projects
are rejected without an automatic migration or destructive recovery.

To inspect the reproducible initial schema from the development shell:

```powershell
dotnet ef migrations script 0 InitialProject --project src/Inklume.Infrastructure --startup-project src/Inklume.Infrastructure --output .local/InitialProject.sql
```

The design-time factory uses an in-memory connection to avoid modifying project
databases while generating migrations. Schema changes must use reviewed migrations.

OCR, AI providers, translation, browser integrations, image processing,
background jobs, exports, and installers belong to later stages.
