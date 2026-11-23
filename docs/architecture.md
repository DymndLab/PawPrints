# Code structure

PawPrints has one runtime assembly, `PawPrints.dll`, installed under `mods/PawPrints/` with mod ID `PawPrints`. The integration layer captures game data; the renderer and document model operate on detached copies.

## Data flow

1. `BlueprintMod.cs` registers the mod, menu and shortcut, manages the fullscreen editor and owns render-job cancellation and preview lifetime.
2. `CraftCapture.cs`, `FuelHoseCapture.cs`, `PoseCapture.cs` and `PerformanceCapture.cs` read the loaded craft into detached drawing data. Fuel hoses use private native routes and copied samples because they have no ordinary part-model mesh. Capture depends on the inspected native KSA APIs.
3. `Rendering/` projects that data, plans placement and scale, extracts linework and composes page backgrounds, drawings, annotations and the title block.
4. The editor uploads the rendered preview, handles interaction and saves PNGs. Print-only poses and explosions do not alter live craft transforms.

## Editor code

`BlueprintMod` is a partial class. Files named `*Controls.cs` and `*Editor.cs` divide the interface by feature, but share its editor state. Render jobs, selection and document history are coordinated through that shared state.

| Area | Entry points |
| --- | --- |
| Generation, recovery and template selection | `WorkspaceHeader.cs`, `GenerationControls.cs`, `PrintRecoveryControls.cs`, `TemplateGallery.cs`, `BoxLayoutControls.cs` |
| Main views, projection links and scale | `DrawingControls.cs`, `ViewSizeControls.cs`, `ViewAlignmentControls.cs` |
| Annotations and tables | `PlacementControls.cs`, `PreferencesControls.cs`, `AnnotationEditor.cs`, `AnnotationActions.cs`, `TableEditor.cs` |
| Exploded views and print-only movement | `ExplodedEditor.cs`, `PoseControls.cs` |
| Free text and selection formatting | `FreeTextControls.cs`, `TextSelectionControls.cs`, `TextPreviewControls.cs` |
| Metadata, logos and warnings | `TitleBlockControls.cs`, `LogoPicker.cs`, `WarningControls.cs` |

The renderer's document/history operations and the editor's transient gestures have different lifetimes. Keep pending placement, accepted composition and asynchronous render results separate. A failed generation must retain the last accepted print.

## Rendering code

- `BlueprintRenderer.cs` and `Sheet.cs` coordinate rendering and sheet data.
- `PrintGeneration.cs` renders detached candidates for ordinary regeneration or an explicitly confirmed fresh drawing. `PrintRecoveryControls.cs` restores accepted settings without rendering when pending changes block editing. Failed candidates retain the previous print.
- `PartSupport.cs` loads the shared `part-support.json` definitions once per capture. `CraftCapture`, `PartNames`, pose labels and engine labels use that same immutable rule set.
- `DrawingPlacement.cs`, `ViewAlignment.cs` and `ScaleAdjustment.cs` handle main-view placement, projection relationships and fit limits. Main views share one physical scale; details have their own scale.
- `ExplodedDetail.cs`, `ExplodedPainting.cs` and `DetailPartSizing.cs` keep assembly spread separate from part size.
- `Annotations.cs`, `LeaderPainting.cs` and `SpecLeaderRouting.cs` handle annotation geometry and consistent leader styling.
- `PartPoses.cs` applies captured movement to detached geometry, including scope and symmetry.
- `FuelHoseGeometry.cs` converts captured native spline frames into bounded tube geometry. `Mesh.ConnectedParts` records endpoint/connector pose dependencies; incompatible independent print poses fail instead of showing a false connection. Exploded hoses follow their owning first fuel port.
- `PerformanceData.cs` and `PerformancePainting.cs` separate captured metrics from their presentation. Main-nozzle gimbal uses both axes from the same highest-thrust nozzle; aggregate thrust and efficiency retain all contributing nozzles.
- `PageLayout.cs`, `FrameLayout.cs`, `SheetZones.cs` and the title-block files define paper geometry and metadata presentation.

`WindowsPngDecoder.cs` validates logo bounds and PNG chunks before calling Windows WIC. `CompanyLogo.cs` classifies monochrome artwork before resizing; `ImageResampling.cs` preserves alpha edges and filters desktop exports. `Png.cs` writes the exported image without an external image library.

Most rendering code has no game API dependency. The synthetic test project compiles these files directly, which lets geometry and layout checks run without KSA.

## Profile storage

`Storage/ProfileStorage.cs` resolves writable paths from KSA's already-resolved profile directory. Logos and title defaults live under `PawPrints/Logos`; the chosen export destination is stored in `PawPrints/export-settings.json`. First opening prompts for a destination; missing destinations cause a save error instead of being recreated. Legacy mod-local data is copied without overwriting profile data or changing installed files. This keeps Borea-managed mod content separate from user data.

## Preview updates and text

`LivePreviewControls.cs` schedules debounced settings updates only when enabled. `Rendering/PreviewUpdates.cs` selects cached appearance composition or geometry rebuilding. Generate PawPrint explicitly recaptures the craft. One active worker and cancellation keep old jobs from becoming accepted documents; UI rendering and texture uploads remain on the game thread.

`RichText.cs` and `FreeText.cs` store formatted ranges and lay out text. The editor edits those ranges in a single styled surface. `TextBoxPlacement.cs` checks the full box bounds, snapping and collisions independently of the optional visible outline. `AnnotationDocument` holds undo/redo snapshots of accepted composition changes.

`Storage/TitlePreferences.cs` and `Storage/ExportPreferences.cs` validate bounded settings files. `PrintDiagnostics.cs` records bounded, local action diagnostics. `PrintUiText.cs` keeps user content away from native printf-style format strings.

## Builds and acceptance

`tools/configure.py` writes ignored local prerequisite paths. `tools/build.py --stage` checks the inspected game hashes, builds and packages the runtime without launching or updating a game profile. `Directory.Build.props` and `Directory.Build.targets` supply and validate explicit game/loader paths.

`tests/Blueprint.Tests.csproj` is a console regression runner, not an xUnit project. The default runner covers portable synthetic suites; explicit --fixtures checks require separately provisioned game-derived data and fail clearly when it is absent. `tests/Blueprint.Acceptance.csproj` is a separate game-side driver; building it does not establish native acceptance.

Optional capture helpers live under `tools/capture/`; their output stays in ignored `tests/fixtures/`. They and their Python dependencies are not runtime components. Capture fixtures must be supplied locally and are excluded from version control.

## Maintenance boundaries

Use `.editorconfig` for C# formatting. Prefer one operation per line and named intermediate values when a long expression obscures a state transition. Explain native API assumptions and coordinate systems where they affect behavior.

Changes to shared editor state, interaction dispatch or render scheduling require focused history/cancellation regressions and native interaction checks. Worker tasks must not retain live game objects, and stale results must not replace newer accepted edits.
