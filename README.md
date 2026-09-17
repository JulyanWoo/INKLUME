# INKLUME

AI Comic Localization Studio for Windows, intended for assisted localization
of manhwa, manga, webtoons, and comics.

This early version creates and reopens local localization projects and imports
ordered chapter images from local folders. OCR and comic processing are not implemented yet.

## Stack and requirements

- Windows x64 and .NET SDK 10.0.301 (compatible patches in the 10.0.3xx band).
- C#, .NET 10, WPF, CommunityToolkit.Mvvm, and WPF UI 4.3.0.
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

Package versions are centralized in `Directory.Packages.props` so the five test
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
    Inklume.Desktop.Tests/
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
SQLite/filesystem round trips, including chapter import, invalid data, cancellation,
and preservation of existing files. Tests own and clean unique directories under their ignored build output.
Imaging.Tests remains configured without cases until imaging is implemented.

## Projects and local chapters

The Fluent desktop shell uses a compact integrated title bar and application menu.
The New Project dialog collects the project name, series name, and absolute folder path.
**Browse** selects an existing folder; a new folder may also be entered if its parent
already exists. Files are written directly inside the selected folder. Project roots
are not required to be empty; INKLUME safely initializes its reserved resources alongside
existing user files and folders without overwriting unrelated content. Names are metadata,
not file paths. Projects opened during the current application session appear under
Recent Projects. In a workspace, **File > Close Project** returns to Home without
deleting project files.

Appearance settings are available from Home and **Tools > Settings**. Graphite,
Midnight, Amethyst, and Paper can be applied while the application is running.
The selected theme is stored in the user's local application data as an application
preference; it is not written into localization projects. Invalid or unknown theme
settings fall back safely to Graphite.

Select **Open Project** and choose the same folder to recover its information,
including after restarting INKLUME. The workspace shell displays the active project,
hybrid project explorer hierarchy, RAW page preview, selected-page metadata, and operation
output. The Output region starts collapsed and can be toggled through
**View > Output**. Closing during an operation requests cancellation and waits for
it to finish.

With a project open, select **Import Chapter**, enter a chapter number and optional
title, and choose a local source folder. Chapter numbers are non-negative decimals,
so both `10` and `10.5` are valid. PNG, JPG, JPEG, and WebP files are supported.
Other files in the selected folder are reported and ignored.

Images are sorted naturally (`1`, `2`, `10`), copied without conversion, and named
sequentially inside the project. INKLUME records the original file name and a
SHA-256 content hash. The source files are never moved, renamed, modified, or deleted.
The Project Explorer presents an IDE-like hybrid tree reflecting project resources
alongside safe user content. Lazy expansion keeps opening times instantaneous even for
large projects with hundreds of chapters or thousands of pages. Persistent Chapter and
Page records are restored from SQLite, while internal application caches (`cache/`) remain
hidden by default. Long names are automatically truncated with an ellipsis instead of
consuming vertical space with horizontal scrollbars; tooltips reveal the full file name
on hover, and the explorer panel can be freely resized using the splitter.

Supported image files (`.png`, `.jpg`, `.jpeg`, `.webp`) that exist physically on disk
outside canonical pages—such as reference images, cover art, or loose images placed
directly under `chapters/`—appear as generic image files. Selecting a generic image file
opens a read-only preview in the Visual Editor without modifying the database or creating
spurious `Page` entities. The Inspector presents real filesystem and pixel metadata for
the unimported image while keeping text region tools safely disabled.

Unimported subdirectories directly under `chapters/` containing supported images are
conservatively recognized as chapter candidates. Users can adopt them via **Import as Chapter...**
from the context menu or action panel, which pre-fills the canonical import dialog while
strictly reusing the standard atomic import pipeline and preserving pre-existing files intact.

## Visual editor

Selecting a canonical page opens the RAW visual editor in editing mode. The editor supports
fit-to-view, 30%-200% interactive zoom, cursor-centered zoom with **Ctrl + Mouse Wheel**, vertical
wheel pan, horizontal pan with **Shift + Mouse Wheel**, middle-button drag, and
**Space + Left Mouse Button** drag. Fit-to-view may use a scale below 30% so a long
webtoon page can remain fully visible. Pan and zoom capabilities are also available when
previewing generic unimported images.

For canonical pages, use **Select**, **Rectangle**, or **Polygon** to work with manual text
regions. Rectangle regions are drawn by dragging. Polygon points are added with clicks and
finished with a double-click or Enter; Escape cancels transient drawing. Delete removes the
selected region. Region role and container type can be edited in the Inspector. All geometry
is stored in immutable RAW-image pixel coordinates, while zoom, pan, and window coordinates
remain temporary UI state. When viewing generic images, region editing tools are disabled
to prevent accidental modifications.

```text
<project root>/
    project.db
    context/
        series.json
        characters.json
        glossary.json
        translation_rules.json
    chapters/
        001/
            001_raw/
                001.png
                002.jpg
        002/
        downloads/     (unknown user directory)
    cache/             (internal cache, hidden by default)
    references/        (user content preserved intact)
    cover.png          (user content preserved intact)
    notes.txt          (user content preserved intact)
```

Reserved project resources include `project.db`, `context/`, `chapters/`, and `cache/`.
Any pre-existing user files or directories (such as `references/`, `notes.txt`, or cover images)
outside or alongside these resources are strictly preserved and never moved, renamed,
or deleted. Pre-existing directories inside `chapters/` that are not managed by INKLUME
remain intact as generic folders; manually placed folders are not automatically imported
as chapters.

SQLite stores `Projects`, `Chapters`, `Pages`, `TextRegions`, and ordered
`TextRegionPoints`. Chapter numbers are unique within a project, while page order
and relative paths are unique within a chapter. Page records contain metadata,
SHA-256 hashes, and RAW pixel dimensions, never image blobs or absolute paths.
Older pages receive dimensions lazily when first opened in the visual editor.
Reading order is one-based and unique within each page.
EF Core also maintains migration history and its migration lock table. Paths are
resolved from the current project root, so a complete project can be moved while
INKLUME is closed.

All four JSON files are UTF-8 documents with `formatVersion: 1` and `projectId`.
`series.json` additionally contains `name`. The other documents contain only those
headers; character, glossary, and translation-rule features are not implemented.

Opening validates the SQLite application identifier, migration history, metadata,
required directories, and JSON identity and format. Known older schemas are migrated
forward before normal read-only access. A folder containing an arbitrary or corrupt `project.db`
is rejected and never overwritten. Supported paths are local absolute paths without traversal,
junctions, or symbolic links. Context files are limited to 1 MiB each in this initial format.

Existing valid projects are detected and prevented from being overwritten during initialization.
Failed initialization safely rolls back only newly created files and directories, leaving
pre-existing user files intact. Select another folder to retry if an unrecoverable conflict exists.

## Migrations

`dotnet-ef` is pinned in the local tool manifest. `InitialProject` creates the
project schema and SQLite application identifier. `AddChaptersAndPages` adds chapter
and page metadata. `AddVisualEditorFoundation` adds compatible page dimensions plus
text-region and point persistence. Unknown or future migration histories are rejected
without destructive recovery.

To inspect the reproducible current schema from the development shell:

```powershell
dotnet ef migrations script 0 AddVisualEditorFoundation --project src/Inklume.Infrastructure --startup-project src/Inklume.Infrastructure --output .local/Inklume.sql
```

The design-time factory uses an in-memory connection to avoid modifying project
databases while generating migrations. Schema changes must use reviewed migrations.

Local folder acquisition is separate from the application import core. The core
accepts an ordered collection of named image streams, allowing later legitimate
sources to reuse the same validation, copy, hash, persistence, and progress behavior.
No website or browser source is implemented.

Text regions can be manually created or automatically detected through OCR.
Automatic detection marks regions with Origin = OcrDetection, while manual annotations
retain Origin = Manual. Re-running OCR on a page atomically replaces only automatic detections,
strictly preserving manual regions and their annotations. Fallback OCR is also available on
manually drawn regions via "Recognize Region".
Translation, browser integrations, image inpainting, cleanup, redraw, typesetting,
background jobs, exports, and installers belong to later stages.

## Text detection and OCR foundation

INKLUME includes a local, offline OCR engine based on PaddleOCR running in a separate,
long-lived Python worker process.

### Architecture

```text
Visual Editor / Inspector (WPF)
    ↓
OcrService (Inklume.Application)
    ↓
PaddleOcrProvider (Inklume.Imaging)
    ↓
OcrWorkerClient (JSON Lines via stdin/stdout)
    ↓
Python Worker (tools/ocr/worker/main.py)
    ↓
PaddleOCR 3.7.0 (PP-OCRv5_mobile_det + korean_PP-OCRv5_mobile_rec)
```

- **Runtime isolation**: PaddleOCR and Python are completely isolated behind the `IOcrProvider` interface in `Inklume.Imaging`. Domain, Application, Desktop, and Infrastructure contain zero references to Python or PaddleOCR.
- **Model profile**: Detection uses `PP-OCRv5_mobile_det` and recognition uses `korean_PP-OCRv5_mobile_rec`. Supports Korean, English, and numeric text.
- **RAW coordinate space**: All bounding polygons and coordinates are preserved strictly in RAW image pixels `(0,0)` top-left, independent of viewport zoom or preview scaling.
- **Tiling**: Long manhwa strips (> 4000px height) are automatically tiled vertically with overlap and IoU deduplication to maintain high detection accuracy.
- **Source immutability**: Source images under `SourceRoot` are strictly read-only and never modified. SHA-256 hashes are verified before and after OCR.
- **Worker lifecycle**: The Python worker starts lazily on the first OCR request, initializes models once, handles multiple sequential requests, and shuts down gracefully when INKLUME exits.
- **No API keys or cloud dependencies**: Normal inference runs completely locally on CPU without network calls. Models are stored in `%LOCALAPPDATA%\INKLUME\Models\OCR\`.

### Offline testing guarantee

`dotnet test` **never** starts the Python worker, never loads ML models, and never connects to the internet. Standard unit and integration tests use deterministic test doubles and fake providers.

### Developer OCR setup and smoke testing

To set up the project-local Python environment:

```powershell
. .\scripts\Enter-Development.ps1
.\scripts\Setup-Ocr.ps1
```

This creates a virtual environment in `.local\ocr\.venv` and installs the exact pinned dependencies (`paddlepaddle==3.3.1`, `paddleocr==3.7.0`). Global Python is never modified.

To run the opt-in real OCR smoke test against copyright-safe synthetic fixtures:

```powershell
. .\scripts\Enter-Development.ps1
.\scripts\Smoke-Ocr.ps1
```

## OCR review, source text correction, and region semantics

Phase 6 implements the human review layer between raw OCR machine output and future translation.

### Human review workflow

```text
RAW IMAGE
    ↓
OCR Detection & Recognition
    ↓
Raw OCR Output (OcrRecognition.Text — Read-Only Machine Provenance)
    ↓
Human Review (TextRegion.ReviewedText — Editable Working Text)
    ↓
Reviewed Source Text & Semantic Metadata (Role, ContainerType, ReadingOrder, ReviewStatus)
    ↓
Effective Source Text (Canonical Input for Future Translation)
```

- **Machine provenance vs. Human corrections**: `OcrRecognition.Text` is strictly read-only machine provenance and is never overwritten by user edits. User corrections are stored on `TextRegion.ReviewedText`.
- **Null vs. Empty semantics**: `ReviewedText == null` represents "no human override" (falls back to raw OCR). An intentional empty string `""` overrides OCR with empty text.
- **Effective source text rule**: The canonical effective source text exposed to future translation is `ReviewedText ?? RawOcrText ?? null`. Manual regions without OCR can receive reviewed text directly.
- **Review status**: Regions are marked `Pending` (0) or `Reviewed` (1). Page review progress (`Reviewed N / M`) is derived dynamically from persisted state without storing redundant aggregate counters.
- **User modification tracking**: An explicit `UserModifiedAt` timestamp records user edits (text changes, classification, status changes, and reordering). Untouched automatic detections retain `UserModifiedAt = null`.
- **Manual semantic classification**: Regions can be classified manually into `TextRegionRole` (Unknown, Dialogue, Thought, Narration, Sfx, System, Signage, Caption, Other) and `TextContainerType` (Unknown, None, SpeechBubble, ThoughtBubble, NarrationBox, SystemPanel, DecorativeShape, Other). No automated AI inference or heuristic guessing is applied.
- **Safe atomic reading order**: Reading order can be adjusted using Move Up and Move Down actions. Updates are executed inside an atomic transaction with a two-phase positive-offset strategy, preventing collisions with `(PageId, ReadingOrder)` unique constraints while respecting `ReadingOrder > 0` check constraints.
- **Reading order navigation**: The Visual Editor supports cycling through regions on the active page via Previous Region and Next Region navigation.
- **Safe OCR rerun**:
  - **Region OCR rerun**: Re-running OCR on a single region replaces `OcrRecognition` but strictly preserves `ReviewedText`, `ReviewStatus`, `ReviewedAt`, `UserModifiedAt`, `Role`, `ContainerType`, `ReadingOrder`, and geometry.
  - **Page OCR rerun**: Re-running Page OCR replaces only untouched automatic detections. Regions with `Origin == Manual` or with user modifications (`UserModifiedAt != null`, `ReviewStatus == Reviewed`, `ReviewedText != null`, or non-unknown Role/Container) are strictly protected.
  - **Overlap suppression**: When new OCR detections overlap an existing protected region with bounding $\text{IoU} \ge 0.50$, duplicate automatic regions are suppressed.
- **Editor SRP refactoring**: Region review presentation and actions are encapsulated in a focused `RegionReviewViewModel` child component, keeping `VisualEditorViewModel` cohesive.
- **Keyboard safety**: When editing reviewed text in the Inspector, text editing shortcuts (Delete, Backspace, Ctrl+A, Ctrl+C, Ctrl+V, Enter) function standardly within the TextBox without triggering canvas commands like Delete Region. Ctrl+Enter saves the active text draft.

