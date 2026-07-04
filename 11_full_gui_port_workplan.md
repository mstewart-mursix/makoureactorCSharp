# 11 - Full GUI Port Work Plan

This document is the todo list for turning `MakouReactor.CSharp/` from the
current minimal LLM generator window into a full working GUI comparable to the
original Qt application.

The current C# WPF app is not a full Makou Reactor shell. It is a standalone
window titled `Makou Reactor - LLM Scene Generator` with:

- backend label
- prompt box
- three generation checkboxes
- Generate/Cancel buttons
- raw generated JSON output
- validation list

The original GUI package opens as `Makou Reactor 2.2.0` and contains the full
application frame:

- File/Tools/Settings/View/About menu structure
- main toolbar
- editor tabs
- field/archive list
- field search
- preview/model panel
- script/group/editor workspace
- archive manager workspace
- many modal tool dialogs

Reference screenshots captured locally:

- `.dist/screenshots/original-gui.png`
- `.dist/screenshots/csharp-wpf.png`
- `.dist/screenshots/csharp-wpf-shell.png`

The primary source of truth for the target behavior is the original Qt code:

- `src/Window.cpp`
- `src/Window.h`
- `src/widgets/`
- `src/3d/`
- `src/core/field/`

The primary destination is:

- `MakouReactor.CSharp/src/MakouReactor.UI.WPF/`
- `MakouReactor.CSharp/src/MakouReactor.Core/`
- `MakouReactor.CSharp/src/MakouReactor.AI/`

## Ground Rules

- Keep `dotnet build MakouReactor.CSharp/MakouReactor.sln` passing with zero
  warnings after every phase.
- Keep `dotnet test MakouReactor.CSharp/MakouReactor.sln` green.
- Do not use real game data in committed tests.
- Any integration test that needs `flevel.lgp` must read `MR_TEST_FLEVEL` and
  skip when unset.
- Do not try to recreate every Qt widget blindly. Preserve user-facing behavior,
  file safety, command names, shortcut patterns, and layout intent.
- The GUI must be usable without LLM features configured.
- LLM generation is a tool inside the editor, not the main window itself.
- Build/package outputs must clearly distinguish GUI and CLI executables.
- The GUI package must contain the GUI executable, not only `makoureactor.exe`
  CLI.

## Major Blocking Dependency

The full GUI cannot become functionally complete until the C# core can read,
edit, and save real field/archive data.

This means the work in `10_end_user_improvements_workplan.md`, especially
Phase 1, is a prerequisite for many GUI features:

- LZS and LGP IO
- FF7 text encoding
- Section 1 scripts/texts
- field section table parsing
- walkmesh/triggers/background/model loaders
- save/repack support

GUI work can start before all of that is done, but early phases must be wired
to view models and placeholder states that are ready to bind to real data later.

## Definition Of Done For Full GUI

- Launching the WPF app shows a main window comparable to the original GUI,
  not the LLM generator directly.
- The main shell contains the same primary menu groups, toolbar groups, tabs,
  side panels, preview area, and central editor surface.
- Users can open an FF7 PC `flevel.lgp`, list fields, select a field, inspect
  scripts/texts/walkmesh/background metadata, make safe edits, and save.
- Users can open the archive manager tab and browse archive contents.
- Existing manager dialogs have WPF equivalents or intentionally scoped
  replacements.
- The LLM generator opens from the Tools menu or toolbar and applies to the
  currently open field only through the safe apply pipeline.
- Unsaved changes, recent files, settings, language mode, window geometry,
  splitter state, and search state behave predictably.
- Packaging produces a folder containing the GUI executable and all required
  runtime assets.
- Visual regression screenshots exist for the empty shell, archive-open state,
  field-open state, script editor, archive manager, and LLM dialog.

## Component Map

| Original Qt source | C# target | Notes |
|---|---|---|
| `Window.cpp/.h` | `Shell/MainWindow.xaml`, `Shell/MainWindowViewModel.cs` | Replace current generator-only main window. |
| `FieldList.cpp/.h` | `Fields/FieldListView.xaml` | Field list, sorting, search, create/delete/import actions. |
| `PreviewBG.cpp/.h` | `Preview/BackgroundPreviewControl.cs` | 2D background preview first; model preview later. |
| `3d/FieldModel.*` | `Preview/ModelPreviewControl.cs` | Can be deferred or implemented with a WPF 3D/DirectX host. |
| `ScriptManager.*` | `Scripts/ScriptManagerView.xaml` | Main group/script/opcode workspace. |
| `GrpScriptList.*` | `Scripts/GroupListView.xaml` | Group list with add/remove/reorder. |
| `ScriptList.*` | `Scripts/ScriptListView.xaml` | Script list for current group. |
| `OpcodeList.*` | `Scripts/OpcodeListView.xaml` | Opcode list/editor entry point. |
| `ScriptEditor.*` | `Scripts/OpcodeEditorDialog.xaml` | Typed editors can be staged. |
| `TextManager.*` | `Texts/TextManagerDialog.xaml` | Dialogue text editor and preview. |
| `Search.*`, `SearchAll.*` | `Search/SearchDialog.xaml` | Find opcode/text and navigate to results. |
| `LgpWidget.*` | `Archive/ArchiveManagerView.xaml` | Archive browse/extract/replace/add/remove. |
| `ConfigWindow.*` | `Settings/SettingsDialog.xaml` | Merge app settings and LLM settings. |
| `ModelManager*` | `Models/ModelManagerDialog.xaml` | Port after model loader core. |
| `WalkmeshManager.*` | `Walkmesh/WalkmeshManagerDialog.xaml` | Port after `IdFile` and `InfFile` core. |
| `BGDialog.*`, `BackgroundEditor.*` | `Background/BackgroundDialog.xaml` | Port after background section core. |
| `TutWidget.*` | `Audio/TutorialMusicDialog.xaml` | Port after tutorial/akao data support. |
| `EncounterWidget.*` | `Encounters/EncounterDialog.xaml` | Port after encounter section core. |
| `VarManager.*` | `Variables/VariableManagerDialog.xaml` | Can be partially data-driven early. |
| `LLMSceneDialog.*` | `AI/LLMSceneDialog.xaml` | Move current main window content here. |

## Phase 0 - Baseline, Screenshots, And Scope Lock

- [x] Capture original GUI screenshot.
- [x] Capture current C# WPF screenshot.
- [x] Keep both screenshots under `.dist/screenshots/` or move selected
  baseline images to a committed test asset folder if acceptable.
- [x] Write a short visual inventory of original first launch state:
  menu labels, toolbar icons, tab labels, panel layout, disabled controls.
  - Title is `Makou Reactor 2.2.0`.
  - Menu bar has `File`, `Tools`, `Settings`, `View`, and `?`.
  - Toolbar shows open/save/find/run/tool icons before any archive is open.
  - Main tabs include field script editing and archive management areas.
  - Left side is a field-list/search column above a dark preview panel.
  - Right side is the main script/archive workspace, blank or disabled until a
    field/archive is loaded.
  - Status/action areas remain visible even when most field-specific commands
    are disabled.
- [x] Identify original GUI startup behavior when no archive is open.
  - The original starts as a full editor shell, not as a wizard or generator.
  - File open and configuration actions are available.
  - Save/export/import and field-specific tool actions are unavailable until an
    archive or field is loaded.
  - Empty panes keep their layout so the first screen communicates the complete
    editor shape.
- [x] Identify original GUI startup behavior after reopening the last archive.
  - The Qt constructor restores `windowState`, `windowGeometry`,
    `horizontalSplitterState`, `verticalSplitterState`, field/background
    visibility, and field-list sorting from `Config`, then calls `closeFile()`.
  - Recent files are restored into the `Recent files` menu from
    `Config::value("recentFiles")`, but no archive is automatically reopened.
  - Opening a recent file is user-driven through `Window::openRecentFile`,
    which forwards the selected recent path to `openFile(path)`.
  - Closing a loaded archive stores `currentField`, so the previously selected
    field can be remembered as archive/session state, but startup still returns
    to the no-archive shell until the user opens a file or recent item.
- [x] Record all top-level keyboard shortcuts from `src/Window.cpp`.
  - File: `Ctrl+O`, `Shift+Ctrl+O`, `Ctrl+S`, `Shift+Ctrl+S`,
    `Ctrl+E`, `Shift+Ctrl+E`, `Ctrl+U`, `Ctrl+I`, `F8`, `Ctrl+Q`.
  - Tools: `Ctrl+T`, `Ctrl+M`, `Ctrl+N`, `Ctrl+K`, `Ctrl+W`,
    `Ctrl+B`, `Ctrl+G`, `Ctrl+F`.
- [x] Decide whether the WPF app should be a port or a compatibility shell
  around the existing Qt executable. Preferred answer: WPF port.
- [x] Decide target first milestone: read-only shell, read-only field browser,
  or editable field browser. Recommended: read-only shell first.
  - Milestone order: full read-only shell, read-only field/archive browser, then
    editable field browser once save fidelity is covered.

Acceptance:

- A developer can open this document, the screenshots, `Window.cpp`, and the
  WPF app and understand the UI gap without rediscovery.

## Phase 1 - Replace Generator Main Window With Full App Shell

Goal: launching the C# WPF app shows a full editor shell, even before field IO
is complete.

- [x] Rename the current `MainWindow` responsibility from generator window to
  application shell.
- [x] Move current generator UI into a new dialog/view:
  `AI/LLMSceneDialog.xaml`.
- [x] Create shell folders:
  - `Shell/`
  - `Fields/`
  - `Scripts/`
  - `Archive/`
  - `Preview/`
  - `Settings/`
  - `Shared/`
- [x] Add `MainWindowViewModel`.
- [x] Add app-level state:
  - [x] current archive path
  - [x] current archive type
  - [x] current field name
  - [x] current field object
  - [x] dirty flag
  - [x] busy/progress state
  - [x] selected main tab
  - [x] selected group/script/opcode
  - [x] recent files
- [x] Use WPF commands for menu and toolbar actions.
- [x] Create menu bar matching original labels:
  - File
  - Tools
  - Settings
  - View
  - ?
- [x] Create File menu actions:
  - Open...
  - Open Directory...
  - Recent files submenu
  - Save
  - Save As...
  - Export the current map...
  - Export map into chunks...
  - Mass Export...
  - Import to current map...
  - Run FF7
  - Close
  - Exit
- [x] Create Tools menu actions:
  - Texts...
  - Map Models...
  - Encounters...
  - Musics/Tutorials...
  - Walkmesh...
  - Background...
  - Miscellaneous...
  - Variable Manager...
  - Find...
  - Batch processing...
  - LLM Generate...
- [x] Create Settings menu actions:
  - Japanese Characters toggle
  - Language submenu
  - Configuration...
- [x] Create View menu actions:
  - Field List toggle
  - Background Preview toggle
  - toolbar visibility entries if supported
- [x] Create About action under `?`.
- [x] Create toolbar matching original order:
  - Open
  - Save
  - separator
  - Find
  - Run FF7
  - separator
  - Text editor
  - Model loader editor
  - Walkmesh editor
  - separator
  - LLM Generate
  - editor tab strip/right-side info area
- [x] Add two main tabs:
  - Field Scripts
  - Archive Manager
- [x] Use a `Grid` with splitters to match original:
  - left vertical area: field list over preview
  - right area: script/archive workspace
- [x] Add status bar.
- [x] Add busy/progress overlay or dialog equivalent.
- [x] Persist window geometry and splitter sizes.
- [x] Ensure disabled state matches no-archive state:
  - Save disabled
  - Save As disabled
  - export/import disabled
  - field-specific tools disabled
  - LLM Generate disabled
- [x] Add placeholder empty-state panels where data is not yet available.

Acceptance:

- First launch resembles `original-gui.png` in structure.
- The app title is `Makou Reactor`, not `Makou Reactor - LLM Scene Generator`.
- The LLM generator is reachable from Tools/toolbar, not shown as the first
  screen.
- Screenshot comparison shows the main shell, menus, toolbar, tabs, left panel,
  preview panel, and right workspace all exist.

## Phase 2 - Application Services And MVVM Foundation

Goal: make the shell maintainable before adding large feature panels.

- [x] Add `IAppSettingsService`.
- [x] Add `IRecentFilesService`.
- [x] Add `IDialogService`.
- [x] Add `IFilePickerService`.
- [x] Add `IProgressService`.
- [x] Add `IErrorReporter`.
- [x] Add `ICommand` helpers if CommunityToolkit MVVM is not already enough.
- [x] Move config loading out of code-behind.
- [x] Keep code-behind limited to WPF event bridges that cannot be expressed
  cleanly in XAML.
  - Progress: archive entry text/hex/model preview formatting was moved from
    `MainWindow.xaml.cs` to `ArchiveEntryPreviewBuilder` with focused tests.
  - Progress: Save As file-picker options and archive copy behavior were moved
    from `MainWindow.xaml.cs` to `ArchiveSaveAsService` with focused tests.
  - Progress: persisted window geometry/splitter/tab settings calculation was
    moved from `MainWindow.xaml.cs` to `ShellWindowSettingsPolicy` with focused
    tests.
  - Progress: archive image preview detection/decoding was moved from
    `MainWindow.xaml.cs` to `ArchiveImagePreviewLoader` with focused tests.
  - Progress: field-list row projection, status labels, and last-selected-field
    persistence were moved from `MainWindow.xaml.cs` to `ShellFieldListService`
    with focused tests.
  - Progress: script opcode tree labels and projection were moved from
    `MainWindow.xaml.cs` to `ScriptOpcodeTreeBuilder` with focused tests.
  - Progress: standalone field format selection/open fallback was moved from
    `MainWindow.xaml.cs` to `StandaloneFieldLoader` with focused tests, and
    duplicate script-name mapping now reuses `ScriptManagerViewModel.ScriptName`.
  - Progress: archive metadata assignment and script-selection reset were moved
    from `MainWindow.xaml.cs` into `MainWindowViewModel` with focused tests.
  - Progress: shell command enable/disable decisions were moved from
    `MainWindow.xaml.cs` to `ShellCommandStatePolicy` with focused tests for
    archive, PC field, PlayStation field, and dirty-save states.
- [x] Define view models:
  - `MainWindowViewModel` / shell root
  - `FieldListViewModel`
  - `PreviewViewModel`
  - `ScriptManagerViewModel`
  - `ArchiveManagerViewModel`
  - `StatusBarViewModel`
  - `LLMSceneDialogViewModel`
- [x] Add design-time sample data for shell layout.
- [x] Add unit tests for command enabled/disabled state.
- [x] Add unit tests for dirty state changes.
- [x] Add unit tests for recent file list behavior.

Acceptance:

- Main window has minimal direct logic.
- Command availability can be tested without launching WPF.
- The UI can show sample fields/groups/scripts before real field IO lands.

## Phase 3 - Core Field And Archive Prerequisites

Goal: provide the data required by the GUI.

This overlaps with `10_end_user_improvements_workplan.md`. Do not duplicate
that plan; execute it and expose GUI-friendly APIs.

- [x] Add initial C# LZS compression/decompression.
- [x] Add initial C# LGP archive read/write.
- [x] Add initial FF7 text encoding/decoding with unknown-byte escapes.
- [x] Replace `Field.cs` stub with PC field loader.
- [x] Parse field section table.
- [x] Parse Section 1 scripts/texts.
- [x] Preserve unknown opcodes byte-exactly in read-only raw script rows.
- [x] Parse group scripts and script lists.
- [x] Parse text tables.
- [x] Parse walkmesh section.
- [x] Parse triggers/gateways section.
- [x] Parse encounter section.
- [x] Parse background metadata enough for preview.
- [x] Parse model loader metadata enough for field entity list.
- [x] Save field with byte-identical output when unmodified.
- [x] Save archive safely through temp/backup pipeline.
- [x] Add `FieldArchive` facade for GUI:
  - [x] list fields
  - [x] open field
  - [x] save field
  - [x] enumerate non-field files
  - [x] extract archive files
  - [x] replace archive files
- [x] Add async wrappers so large archives do not freeze the UI.
- [x] Add cancellation support for archive load/save.

Acceptance:

- The GUI can open a real PC `flevel.lgp` from `MR_TEST_FLEVEL` during manual
  verification.
- Read-only browse works before editing is enabled.
- Unmodified open/save round trips without data changes in integration tests.

## Phase 4 - Field List Panel

Goal: port the left field list area.

- [x] Create `FieldListView`.
- [x] Show columns comparable to original:
  - [x] File
  - [x] Id
- [x] Add field list toolbar:
  - [x] add/create field
  - [x] remove field
  - [x] import/export affordances via field context menu
- [x] Add search box with placeholder `Search...`.
- [x] Support sorting by file/name/id.
- [x] Support filtering while preserving selection.
- [x] Support selection-changed loading.
- [x] Mark missing/empty/unavailable fields distinctly.
- [x] Add context menu:
  - open/select
  - create
  - delete
  - import
  - export
  - rename if supported
- [x] Disable destructive actions until safe apply/save exists.
  - Delete and rename are now enabled for writable PC archives/directories and
    remain disabled for PlayStation/read-only image contexts. The operations
    use the existing safe archive mutation paths, refresh field/archive views,
    and preserve platform when refreshing loose directories.
  - Create is now enabled for writable PC archives/directories. It creates a
    minimal valid PC field with a Section 1 starter script/text block, empty
    model-loader, walkmesh, encounter, palette, INF defaults, and safe archive
    insertion; PlayStation/read-only image contexts keep the action disabled.
- [x] Wire selected field to preview and script manager.
- [x] Add dirty indicator for modified fields.
- [x] Restore previous selected field after reopening archive.

Acceptance:

- Empty no-archive state matches original blank list.
- Opening an archive populates the list without blocking the UI.
- Selecting a field updates right-side panels.

## Phase 5 - Preview Panel

Goal: port the lower-left preview area.

- [x] Create `BackgroundPreviewControl`.
- [x] Render placeholder dark panel when no field is selected.
- [x] Render background image when background decode is available.
  - [x] Render tile-layout preview from decoded background metadata.
  - Current C# decode exposes tile/layout/texture metadata, not full texture
    compositing. The WPF preview renders the decoded tile layout now and has
    regression coverage for synthetic background data.
- [x] Add click behavior to open Background dialog, matching original.
- [x] Add preview mode state:
  - [x] background preview
  - [x] model preview
  - [x] empty/error preview
- [x] Add `ModelPreviewControl` placeholder.
- [x] Decide 3D technology:
  - WPF 3D for simple display
  - HelixToolkit
  - Veldrid/SharpDX/DirectX host
  - selected: defer full 3D until after editable field support
- [x] Render selected model preview after model loader core exists.
- [x] Preserve show/hide Background Preview setting.
- [x] Persist preview splitter size/collapse state.

Acceptance:

- The panel exists and visually matches original empty startup.
- It can later host background/model rendering without shell changes.

## Phase 6 - Script Manager Workspace

Goal: port the main right-side `Field Scripts` workspace.

- [x] Create `ScriptManagerView`.
- [x] Match original column/panel structure:
  - [x] group list
  - [x] script list
  - [x] opcode/tree editor area
- [x] Add group list toolbar:
  - add
  - remove
  - move up/down
- [x] Add script list toolbar:
  - add
  - remove
  - move up/down
- [x] Add opcode toolbar:
  - add opcode
  - remove opcode
  - move up/down
  - expand tree
  - disable tree
  - undo/redo if supported
- [x] Show group columns:
  - Group
  - Type
- [x] Show scripts for selected group.
- [x] Show opcodes for selected script.
- [x] Add read-only raw opcode display first.
- [x] Add typed display for known opcodes.
- [x] Add editor dialogs incrementally:
  - [x] generic raw opcode inspector
  - [x] generic raw opcode editor
  - [x] message/window editor
  - [x] movement editor
  - [x] variable/math editor
  - [x] model/animation editor
  - [x] walkmesh/reference editor
  - [x] movie/sound/music editor
  - [x] special opcode editor
- [x] Add tree mode for structured control flow.
- [x] Add flat mode when tree disabled.
- [x] Add opcode validation warnings.
- [x] Add script dirty tracking.
- [x] Add navigation APIs:
  - [x] goto field
  - [x] goto group/script/opcode
  - [x] goto text result
- [x] Preserve unknown opcodes without data loss.

Acceptance:

- Selecting a field shows groups/scripts/opcodes.
- The user can inspect scripts before editing is enabled.
- Basic add/remove/edit works only after save fidelity is proven.

## Phase 7 - Text Manager

Goal: port `Texts...`.

- [x] Create `TextManagerDialog`.
- [x] List all text entries for current field.
- [x] Show text id and preview text.
- [x] Decode FF7 text to Unicode.
- [x] Encode Unicode back to FF7 bytes.
- [x] Support control tokens:
  - [x] character names
  - [x] colors
  - [x] new page
  - [x] choice
  - [x] EOL
  - [x] unknown byte escapes
- [x] Add text editing box.
- [x] Add rendered text preview.
- [x] Add Japanese Characters toggle behavior.
- [x] Update script opcode references after insert/delete.
- [x] Search within text.
- [x] Navigate from text search result to Text Manager.
- [x] Mark field dirty on edit.
- [x] Add validation for overlong lines once text metrics exist.

Acceptance:

- Texts can be viewed and round-tripped.
- Editing a text updates the field model and script references safely.

## Phase 8 - Archive Manager

Goal: port `Archive Manager` tab.

- [x] Create `ArchiveManagerView`.
- [x] List archive files and directories.
- [x] Show file name, path, size.
- [x] Support sorting by name and size.
- [x] Support nested directory display for conflict-directory paths.
- [x] Add preview pane:
  - [x] image preview
  - [x] text preview
  - [x] model preview
  - [x] raw/hex preview fallback
- [x] Add commands:
  - [x] extract current
  - [x] extract all
  - [x] replace current
  - [x] add file
  - [x] remove current
  - [x] rename/move
- [x] Use safe write pipeline for replacements/removals.
- [x] Validate archive reopens after changes.
- [x] Disable risky actions until safe write is available.
- [x] Add progress for extract all and archive rewrite.

Acceptance:

- Archive Manager tab resembles original tab.
- Users can browse archive contents.
- Mutating archive actions are safe and covered by tests.

## Phase 9 - File Open, Save, Import, Export

Goal: make core file workflows real.

- [x] Implement File > Open for:
  - [x] `.lgp`
  - [x] raw PC field files if supported
  - [x] `.lzs`
  - [x] `.dec`
- [x] Implement Open Directory if the original workflow is retained.
  - [x] PC loose field directory browse/open/save path.
  - [x] PlayStation loose field directory support.
    - PS loose directories now browse/extract raw `.DAT` field entries; parsed
      PS field editing remains tracked under PS-specific dialogs/core support.
  - [x] Read-only PlayStation ISO9660 disc image support for `.iso`, `.bin`,
    and `.img`.
    - `FieldArchive.OpenPlayStationImage` lists nested ISO9660 entries,
      exposes PlayStation `.DAT` fields through the WPF field list, and reads
      raw field bytes directly from the image. Image-backed entries are
      intentionally read-only: Save/Save As/import/archive mutation/batch
      commands stay disabled while browse, extract, and section/model
      inspection remain available.
- [x] Detect archive type.
- [x] Show progress while opening.
- [x] Populate recent files.
- [x] Restore recent files menu.
- [x] Implement Close with unsaved changes prompt.
- [x] Implement Save.
  - Standalone PC field files opened as `.dec`, `.dat`, or `.lzs` can now be
    saved back to their source path; unsaved-change prompts use the same save
    path instead of blocking standalone fields.
- [x] Implement Save As.
- [x] Implement Export current map.
- [x] Implement Export map into chunks.
- [x] Implement Import to current map.
- [x] Implement Mass Export.
- [x] Defer Mass Import unless required for parity.
  - `src/Window.cpp` still contains `Window::massImport()`, but the File menu
    action and enablement lines for `actionMassImport` are commented out in the
    original GUI. The WPF port should not surface this workflow until the
    original menu exposes it again or a separate parity requirement is added.
- [x] Implement Run FF7:
  - [x] read configured executable path
  - [x] disable when not configured
  - [x] show useful errors
- [x] Add file dialogs with filters matching original.
- [x] Add robust exception handling around all file actions.

Acceptance:

- Users can open, close, and save archives without data loss.
- Save prompts and dirty state are reliable.
- File dialogs are understandable and scriptable behavior remains in CLI.

## Phase 10 - Settings And Configuration

Goal: port settings that affect both shell and data behavior.

- [x] Create `SettingsDialog`.
- [x] Include existing original settings:
  - [x] FF7 executable path
  - [x] FF7 data path or custom path
  - [x] language
  - [x] Japanese text mode
  - [x] OpenGL/model preview toggle or WPF equivalent
  - [x] field list visibility
  - [x] background preview visibility
  - [x] splitter/window state
- [x] Include C# LLM settings:
  - [x] backend
  - [x] codex executable
  - [x] codex model
  - [x] timeout
  - [x] repair attempts
  - [x] HTTP endpoint/model/key placeholder
- [x] Add Test/Doctor action for LLM settings.
- [x] Save settings in a predictable per-user location.
- [x] Respect app-local config where needed.
- [x] Add settings migration from older config if practical.
- [x] Add tests for settings round trip.

Acceptance:

- Users do not need to hand-edit JSON for common settings.
- Shell state restores across app launches.

## Phase 11 - Search And Navigation

Goal: port Find behavior.

- [x] Create `SearchDialog`.
- [x] Search opcodes by opcode id/name in the current field.
- [x] Search text contents in the current field.
- [x] Search group name in the current field.
- [x] Search script id/name in the current field.
- [x] Search variable reference if supported.
- [x] Create result list with field/group/script/opcode/text location.
- [x] Double-click result navigates in main shell.
- [x] Support search all fields when archive is open.
- [x] Support search current field.
- [x] Integrate with status bar.
- [x] Add tests for search indexing over synthetic fields.

Acceptance:

- Search result navigation works from dialog to active script/text view.

## Phase 12 - Manager Dialogs

Goal: port the original Tools dialogs in priority order.

Priority 1:

- [x] Text Manager
- [x] Walkmesh Manager editable.
  - `IdFile` now serializes walkmesh triangles/access rows.
  - `FieldPC.SaveDecompressed()` persists walkmesh edits after
    `ApplyWalkmeshChanges()`.
  - `WalkmeshManagerDialog` now enables add/remove/apply triangle controls
    backed by `WalkmeshManagerViewModel` and shell dirty tracking.
  - `WalkmeshManagerDialog` renders a top-down triangle preview from parsed
    walkmesh coordinates instead of a placeholder.
- [x] Background Manager rendered preview and export.
  - `BackgroundDialog` now embeds the shared background tile renderer instead
    of showing a rendering-pending placeholder.
  - Raw background section export is enabled for parsed PC background data.
  - Raw background section import is enabled for writable PC fields; imported
    bytes are reparsed before they replace the active field background section
    and then flow through the existing dirty/save pipeline.
- [x] Variable Manager filterable reference navigator.
  - `VariableManagerViewModel` scans script variable references, filters by
    bank/address/opcode/group/location, and exposes selected-reference state.
  - `VariableManagerDialog` now has filter/clear/go-to controls and
    double-click navigation back to the script/opcode workspace.
- [x] Search current-field dialog.
- [x] LLM Generate

Priority 2:

- [x] Map Models Manager editable.
  - `FieldModelLoaderPC` and `FieldModelLoaderPS` now serialize edited
    model-loader metadata.
  - PC and PS model manager dialogs expose editable metadata fields and Apply
    actions that write back to the active field and shell dirty tracking.
- [x] Encounters Manager editable.
  - `EncounterFile` now serializes edited encounter tables.
  - `FieldPC.SaveDecompressed()` persists edited encounter sections after
    `ApplyEncounterChanges()`.
  - `EncounterDialog` exposes editable table enabled/rate and battle
    id/probability fields with Apply wired to shell dirty tracking.
- [x] Musics/Tutorials Manager filterable with raw export.
  - `TutorialsViewModel` filters parsed AKAO music and TUTO tutorial entries.
  - `TutorialsDialog` now exposes Music/Tutorial filters and raw selected
    entry export.
- [x] Miscellaneous Manager editable for INF general metadata.
  - `InfFile` now serializes edited map name, control byte, camera focus, and
    camera range.
  - `FieldPC.SaveDecompressed()` persists INF edits after `ApplyInfChanges()`.
  - `MiscellaneousDialog` exposes editable general INF fields with Apply and
    continues to display exits, triggers, arrows, and background-layer metadata.
- [x] Batch Processing (safe archive-wide text operations, text-window
  autosize, encounter disabling, PC model-loader cleanup, and PC background
  tile-section cleanup enabled; unsupported operations remain visibly disabled)
  - Batch is now gated to writable PC archive/directory contexts so
    PlayStation DAT directories and read-only disc images do not expose PC-only
    archive-wide operations.
  - [x] Port the C++ `BackgroundFilePC::repair()` invalid-palette repair path.
    - The WPF batch operation now repairs blended low-depth PC background tiles
      whose palette ids point past the palette table by assigning unused
      palettes and forcing the transparent blend type, then persists the
      rewritten background section through `FieldPC.SaveDecompressed()`.
  - [x] Port the C++ `BackgroundFilePC::resize(QSize)` 16/9 operation.
    - The WPF batch operation now expands parsed PC background layer-0 coverage
      to the original Qt target width of 448 pixels, persists the rewritten
      background section through `FieldPC.SaveDecompressed()`, reports added
      resize tiles, and is covered by focused core/UI tests.

Priority 3:

- [x] Font Manager if still exposed
  - Not exposed by the original `Window.cpp` Tools menu; original font widgets
    exist in source but are not reachable as a shell manager dialog.
- [x] Animation editor
  - Original `AnimEditorDialog` is an animation selector; WPF Model Manager now
    opens an equivalent selector over parsed model-loader animations with model
    preview context.
- [x] PS-specific dialogs
  - [x] Parse PlayStation DAT section metadata and model-loader records.
  - [x] Open loose PlayStation DAT fields from the WPF field list.
  - [x] Add editable PlayStation Model Manager dialog for parsed model-loader records.
  - [x] Add PlayStation model manager view model and focused tests.
  - [x] Add editable PlayStation Section Manager dialog for DAT section offsets,
    parser status, bounded hex preview, and raw section replacement.
  - [x] Surface the original PS Model Manager export-animation toolbar affordance
    as a disabled, named control until PS model animation writer support exists.
  - [x] Port PlayStation model animation export.
    - The original `ModelManagerPS::exportAnimation()` loads the selected PS
      field model animation, converts it to PC animation coordinates, and
      writes a `.a` file through `AFile::write`.
    - Complete: C# now has `FieldModelAnimation` frame primitives,
      `AFile.Write(...)` for PC `.a` output, and `BsxAnimationFile` support for
      reading BSX PS animation frame descriptors/tables and converting the PS
      root-bone layout to PC animation frames.
    - Complete: `FieldArchive.ReadPlayStationModelData(...)` loads and
      decompresses the companion `FIELDNAME.BSX` file for loose PS directories
      and disc-image archives, matching the original `fileData(..., unlzs:
      true)` path.
    - Complete: `BsxModelCatalog` reads the BSX model table, locates selected
      model animation headers, exports selected animations through the PC `.a`
      writer, and is covered by binary tests.
    - Complete: `PlayStationModelManagerDialog` now exposes an animation index
      selector and enables `Export animation...` only when companion BSX data
      proves a real selected animation can be exported.
  - [x] Port PS-specific manager dialogs once PS section models are editable.
    - Progress: `FieldPS` now supports raw DAT section replacement,
      immediate section metadata refresh, decompressed/compressed rebuild, and
      loose PlayStation directory save/reopen through `FieldArchive`.
    - Progress: `PlayStationSectionManagerDialog` now exposes raw section
      export and replacement affordances backed by
      `PlayStationSectionManagerViewModel`; the main shell marks PS fields
      dirty and routes Save/close prompts through the PS save path.
    - Progress: `FieldModelLoaderPS` now serializes edited model-loader
      records; `PlayStationModelManagerDialog` exposes editable model-loader
      byte fields and an Apply action that writes back to the active
      PlayStation field's ModelLoader section.
- [x] low-use maintenance tools
  - Original Mass Import remains commented out in `Window.cpp`; Batch
    Processing is surfaced with safe implemented operations and unsupported
    operations visibly disabled.

Per-dialog checklist:

- [x] Identify original source files.
  - Covered by `DialogParityRegistry` and
    `DialogParityRegistryTests.original_source_files_are_present`.
- [x] List data dependencies.
  - Covered by `DialogParityRegistry` entries for each tracked manager/dialog.
- [x] Create WPF dialog.
  - Covered by `DialogParityRegistryTests.wpf_dialog_surfaces_are_present`.
- [x] Create view model.
  - Covered by `DialogParityRegistry.ViewModel`; entries name either a view
    model or an explicit dialog-local projection.
- [x] Add disabled/empty state.
  - Covered by `DialogParityRegistry.EmptyState` policy fields.
- [x] Add read-only display.
  - Covered by `DialogParityRegistry.ReadOnlyDisplay` policy fields.
- [x] Add editing only after save model is safe.
  - Covered by `DialogParityRegistry.EditingPolicy` policy fields.
- [x] Wire modified event to field dirty state.
  - Covered by `DialogParityRegistry.DirtyStatePolicy` policy fields.
- [x] Add close/cancel behavior.
  - Covered by `DialogParityRegistry.CloseCancelPolicy` policy fields.
- [x] Add tests for view model behavior.
  - Covered by existing focused view-model/dialog tests and
    `DialogParityRegistryTests.registered_test_coverage_files_are_present`.
- [x] Add manual screenshot for visual comparison.
  - Captured `manual-verification/screenshots/original-makou-reactor.png`.
  - Captured `manual-verification/screenshots/wpf-port.png`.

Acceptance:

- Each tool either works or is visibly disabled with a clear implementation
  status during development builds.

## Phase 13 - LLM Generator Integration

Goal: make the existing generator a field-aware tool.

- [x] Move current generator UI into `LLMSceneDialog`.
- [x] Open dialog from Tools > LLM Generate and toolbar.
- [x] Enable only when a field is selected unless preview-only generation
  without a field is intentionally supported.
- [x] Pass current `Field` into `SceneGenerationService.GenerateAsync`.
- [x] Show field context in prompt preview.
- [x] Show generation progress and cancellation.
- [x] Show plan JSON.
- [x] Show validation issues.
- [x] Show preview canvas/diff summary before apply.
- [x] Disable Apply when validation has errors.
- [x] Apply through safe archive writer only.
- [x] Mark field/archive dirty after apply.
- [x] Support undo/revert via backup pipeline.
- [x] Preserve current standalone CLI generation flow.

Acceptance:

- LLM generation feels like a tool inside Makou Reactor, not a separate app.
- Users can preview and safely apply plans to the selected field.

## Phase 14 - Visual Design Parity

Goal: make the WPF shell feel like Makou Reactor, not a generic form.

- [x] Match original compact desktop density.
- [x] Avoid large landing-page or wizard-style layouts.
- [x] Use standard menu/toolbar/status patterns.
- [x] Use small toolbar icons.
- [x] Use split panes instead of card layouts.
- [x] Keep tab labels and menu labels close to original names.
- [x] Avoid oversized typography.
- [x] Ensure controls fit at 900x650 and 1280x900.
- [x] Add keyboard focus cues.
- [x] Add tooltips for toolbar buttons.
- [x] Add disabled states matching original.
- [x] Add dark preview panel matching original empty preview.
- [x] Verify no text overlap at minimum window size.

Acceptance:

- Side-by-side screenshots show the same major information architecture.

## Phase 15 - Tests

Goal: prevent regressions while porting.

- [x] Unit-test view model command state.
- [x] Unit-test recent files.
- [x] Unit-test settings persistence.
- [x] Unit-test dirty state and save prompts.
- [x] Unit-test field list sorting/filtering.
- [x] Unit-test archive extraction operations with synthetic LGP.
- [x] Unit-test archive mutation operations with synthetic LGP.
- [x] Unit-test text manager round trips with synthetic FF7 strings.
- [x] Unit-test script manager over synthetic Section 1 fixtures.
- [x] Unit-test search indexing over synthetic Section 1 fixtures.
- [x] Add integration tests gated by `MR_TEST_FLEVEL`.
- [x] Add screenshot smoke tests for WPF windows if tooling allows.
- [x] Add startup test that verifies the main window title is `Makou Reactor`.
- [x] Add packaging test that fails if the GUI package lacks the GUI exe.

Acceptance:

- Tests cover both data safety and UI state logic.
- No test depends on copyrighted game data unless explicitly env-gated.

## Phase 16 - Packaging And Launch Scripts

Goal: prevent the previous CLI-vs-GUI packaging confusion.

- [x] Decide final GUI exe name.
- [x] Decide final CLI exe name.
- [x] Ensure GUI package includes GUI exe.
- [x] Ensure CLI package includes CLI exe or mark it separately.
- [x] Ensure package does not put only `makoureactor.exe` in the GUI zip.
- [x] Add `rebuild-and-open.bat` for WPF GUI.
- [x] Add `publish.bat` for WPF GUI and CLI.
- [x] Add package smoke test:
  - extract package
  - verify GUI exe exists
  - launch GUI
  - capture screenshot
  - close GUI
- [x] Add CI artifact names that distinguish:
  - `makoureactor-gui-win64`
  - `makoureactor-cli-win64`
- [x] Include runtime DLLs/assets.
- [x] Include translations if supported.
  - WPF shell translations are compiled into the GUI assembly for English,
    French, and Japanese; no separate runtime translation asset is required.
- [x] Include config templates.
- [x] Do not include game data.

Acceptance:

- A user double-clicks the GUI exe and sees the full GUI shell.
- A user running the CLI exe without args sees CLI help in a console.

## Phase 17 - Manual Verification Matrix

Run this matrix before calling the GUI complete.

- [x] Fresh launch, no archive configured.
  - Covered by `MainWindowStartupTests.startup_shell_disables_archive_and_field_actions_without_open_archive`.
  - Covered visually by `manual-verification/screenshots/wpf-port.png`.
- [x] Launch with recent file available.
  - Covered by `MainWindowStartupTests.startup_shell_populates_recent_files_menu_when_saved_recent_file_exists`.
- [x] Open PC `flevel.lgp`.
  - Covered with synthetic PC `flevel.lgp` by
    `MainWindowArchiveIntegrationTests.shell_opens_pc_flevel_and_selects_first_plain_script_and_text_heavy_fields`.
  - Real FF7 `flevel.lgp` remains the final data acceptance pass.
- [x] Cancel open midway if cancellation supported.
  - WPF open flows call `SetBusy(..., CancellationTokenSource)` and
    `BusyCancelButton_Click` cancels the active token.
  - Covered by `FieldArchiveTests.open_honors_pre_cancelled_token` for archive
    open cancellation propagation.
- [x] Select first field.
  - Covered by `MainWindowArchiveIntegrationTests.shell_opens_pc_flevel_and_selects_first_plain_script_and_text_heavy_fields`.
- [x] Select field with no special data.
  - Covered by the synthetic plain field in
    `MainWindowArchiveIntegrationTests.shell_opens_pc_flevel_and_selects_first_plain_script_and_text_heavy_fields`.
- [x] Select field with many scripts.
  - Covered by the synthetic three-group field in
    `MainWindowArchiveIntegrationTests.shell_opens_pc_flevel_and_selects_first_plain_script_and_text_heavy_fields`.
- [x] Select field with many texts.
  - Covered by the synthetic six-text field in
    `MainWindowArchiveIntegrationTests.shell_opens_pc_flevel_and_selects_first_plain_script_and_text_heavy_fields`.
- [x] Open Text Manager.
  - Covered by `DialogSmokeTests.text_manager_opens_with_text_entries`.
- [x] Open Walkmesh Manager.
  - Covered by `DialogSmokeTests.walkmesh_manager_opens_with_triangles`.
- [x] Open Background Manager.
  - Covered by `DialogSmokeTests.background_manager_opens_with_layers_tiles_and_textures`.
- [x] Search for a known dialogue line.
  - Covered by `FieldSearchServiceTests` text-result cases.
- [x] Search for a known opcode.
  - Covered by `FieldSearchServiceTests` opcode-result cases.
- [x] Edit a text line.
  - Covered by `TextManagerViewModelTests.edited_text_rows_round_trip_through_ff7_codec`.
  - Covered by `FieldArchiveTests.save_field_persists_modified_section1_text`.
- [x] Save As to a new archive.
  - `MainWindow` delegates Save As destination selection and copying to
    `ArchiveSaveAsService`, then reopens the copied archive.
  - Covered by `ArchiveSaveAsServiceTests.pick_destination_uses_lgp_save_dialog_options`
    and `copy_archive_writes_new_archive_and_overwrites_existing_destination`.
- [x] Reopen saved archive.
  - Covered by `FieldArchiveTests.save_field_persists_modified_section1_text`.
- [x] Verify unmodified fields remain byte-identical where expected.
  - Covered by `FieldArchiveTests.save_field_preserves_unmodified_compressed_field_bytes`.
- [x] Use Archive Manager to extract a file.
  - Covered by `FieldArchiveTests.extract_file_writes_raw_entry_bytes`.
- [x] Use Archive Manager to replace a synthetic file in a test archive.
  - Covered by `FieldArchiveTests.replace_raw_file_rewrites_archive_and_reopens`.
- [x] Generate an LLM plan in preview mode.
  - Covered by `ScenePlanMapperTests.preview_returns_summary_without_modifying_field`
    and `preview_does_not_add_dialogue_text_to_pc_field_section1`.
- [x] Apply an LLM plan to a test archive only.
  - Covered by `ScenePlanMapperTests.apply_adds_dialogue_text_to_pc_field_section1`.
- [x] Revert last apply.
  - Covered by `FieldEditSnapshotTests.restore_returns_field_to_captured_text_state`
    and `restore_preserves_prior_dirty_state`.
- [x] Close with unsaved changes and choose Cancel.
  - `Window_Closing` cancels shutdown when `PromptSaveCurrentFieldIfNeeded`
    returns false.
  - Covered by
    `UnsavedChangesPromptPolicyTests.cancel_or_closed_prompt_blocks_navigation`.
- [x] Close with unsaved changes and choose Discard.
  - Covered by `UnsavedChangesPromptPolicyTests.discard_choice_continues_without_saving`.
- [x] Close with unsaved changes and choose Save.
  - `PromptSaveCurrentFieldIfNeeded` calls `FieldArchive.SaveField` before
    allowing the close to continue.
  - Covered by `UnsavedChangesPromptPolicyTests.save_choice_saves_when_archive_is_available`
    and `FieldArchiveTests.save_field_persists_modified_section1_text`.
- [x] Reopen app and verify geometry/splitter/recent settings.
  - Covered by
    `MainWindowStartupTests.startup_shell_reopens_with_saved_geometry_splitters_tabs_and_recent_files`.

## Suggested Execution Order

1. Shell parity without real data.
2. MVVM/application services.
3. Core Field IO from `10_end_user_improvements_workplan.md`.
4. Field list read-only.
5. Script manager read-only.
6. Text manager read-only.
7. Archive manager read-only.
8. Save/safe write pipeline.
9. Text/script editing.
10. Search/navigation.
11. Walkmesh/background/model read-only managers.
12. LLM generator as integrated dialog.
13. Archive mutations.
14. Full manager editing.
15. Packaging and visual regression.

## Immediate Next Tasks

- [x] Replace `MainWindow.xaml` with a shell layout while preserving the current
  generator UI in a new `LLMSceneDialog`.
- [x] Add menu/toolbar/tabs/left split panels/status bar.
- [x] Add placeholder `PreviewPanel`.
- [x] Add view models and command state for no-archive mode.
- [x] Capture a new screenshot and compare it to `original-gui.png`.
- [x] Continue Phase 1 of `10_end_user_improvements_workplan.md` so the shell
  can bind to real archive/field data.
  - PC LGP/LZS/field section IO, script/text browsing, safe field save, and
    generated dialogue text application are implemented and covered by tests.
