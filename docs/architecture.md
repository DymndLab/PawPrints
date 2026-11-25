# Code structure

PawPrints has one runtime assembly, `PawPrints.dll`, installed under `mods/PawPrints/` with mod ID `PawPrints`. The integration layer captures game data; the renderer and document model operate on detached copies.

## Data flow

1. `BlueprintMod.cs` registers the mod, menu and shortcut, manages the fullscreen editor and owns render-job cancellation and preview lifetime.
2. `CraftCapture.cs`, `FuelHoseCapture.cs`, `PoseCapture.cs` and `PerformanceCapture.cs` read the loaded craft into detached drawing data. Fuel hoses use private native routes and copied samples because they have no ordinary part-model mesh. Capture depends on the inspected native KSA APIs.
3. `Rendering/` projects that data, plans placement and scale, extracts linework and composes page backgrounds, drawings, annotations and the title block.
4. The editor uploads the rendered preview, handles interaction and saves PNGs. Print-only poses and explosions do not alter live craft transforms.

Editable drawings use `DrawingProjectControls.cs` and the bounded project codec and store. Across restarts the assembly fingerprint and referenced part addresses must resolve before the loaded document replaces the current one. Final PNG preview and publication use `FinalExportControls.cs` and `Rendering/FinalPng.cs`. The publish check covers the accepted sheet, revision, source lifetime, output dimensions, quality and date.

## Editor code

`BlueprintMod` is a partial class. Files named `*Controls.cs` and `*Editor.cs` divide the interface by feature, but share its editor state. Render jobs, selection and document history are coordinated through that shared state.

| Area | Entry points |
| --- | --- |
| Generation, recovery and template selection | `WorkspaceHeader.cs`, `GenerationControls.cs`, `PrintRecoveryControls.cs`, `TemplateGallery.cs`, `BoxLayoutControls.cs` |
| Main views, projection links and scale | `DrawingControls.cs`, `ViewSizeControls.cs`, `ViewAlignmentControls.cs` |
| Annotations and tables | `PlacementControls.cs`, `PreferencesControls.cs`, `AnnotationEditor.cs`, `AnnotationActions.cs`, `TableEditor.cs` |
| Exploded views and print-only movement | `ExplodedEditor.cs`, `PoseControls.cs` |
| Free text and selection formatting | `FreeTextControls.cs`, `TextSelectionControls.cs`, `TextPreviewControls.cs` |
| Metadata, logos and warnings | `TitleBlockControls.cs`, `LogoPicker.cs`, `ActivityControls.cs` |

The renderer's document/history operations and the editor's transient gestures have different lifetimes. Keep pending placement, accepted composition and asynchronous render results separate. A failed generation must retain the last accepted print.

## Document and render boundaries

`RenderJobControls.cs` routes sheet jobs through `Rendering/RenderJobs.cs`. Each request captures its document identity/revision, source epoch and commit policy. Superseded, cancelled or stale results cannot publish a preview. Source changes are checked before polling completion; closing the workspace cancels pending rendering. Workers receive detached inputs. Native capture remains synchronous on the editor thread; these job contracts do not make native capture incremental.

`DrawingContent.cs` holds compact editable definitions for history, without scene rasters or captured meshes. `AnnotationDocument` advances a revision for edits and history navigation. `DrawingAcceptance.Prepare` prepares a separate document before preview upload; the accepted checkpoint restores definitions and undo/redo stacks after a failed update. Nested definition records are shared as immutable values, retaining the existing editor's equality semantics.

`CraftIdentityCapture.cs` copies part identity and connections into `SourceTopology`. Real structural joints retain both part/connector endpoints, with ownership and fuel links represented separately. Surviving native Part objects retain a source session across tree replacement; newly loaded objects require rebinding even if their numeric IDs or names match. Save-local IDs are traversal identifiers, not persistent part identity. `PrintGeneration` rejects a mismatched source session while preserving the accepted drawing.

`DrawingDefinitions.cs` describes output/canvas dimensions, appearance, projection groups, view scopes and layer order. The existing renderer supplies accepted definitions through an adapter. Its bounded, versioned JSON schemas are internal drafts: layout templates exclude appearance and craft bindings; drawing drafts retain them. These are not user-facing Save/Open functionality or complete migration of every legacy object. Budget/reservation and cache-ownership interfaces declare the next integration boundary; existing capture/material/page limits still enforce resource safeguards.

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

## Projection groups and canvas composition

`ProjectionGroups.cs` resolves shared scales from primary/projection relationships. Standalone views own independent scales. Resize gestures evaluate their opening snapshot, latch modifiers and commit one history operation. Projected extents are retained separately from minimum raster dimensions to preserve resize anchors.

`CanvasObjects.cs` defines document ordering, content bounds and selection. View tiles retain per-view ink/mesh ownership so overlapping layers preserve identity. `CanvasHitShape.cs` records sparse annotation strokes, glyph cells and raster marks during composition, avoiding full panel rectangles in hit testing. `ViewAnchors.cs` preserves part contacts even when a view is off-page or covered.

The editor enables `PrintOptions.FreeLayout`. Collision advice is separate from safety checks in `CanvasBounds.cs`; raster writes and line segments clip to fixed page buffers. Main/detail tiles share a 16,777,216 retained-pixel ceiling, in addition to individual tile/page/geometry limits. This bounds one retained allocation category; it is not a benchmark of total process memory or large-craft performance. Native interaction acceptance remains necessary after changes to these paths.

## Profile storage

`Storage/ProfileStorage.cs` resolves writable paths from KSA's already-resolved profile directory. Logos and title defaults live under `PawPrints/Logos`; the chosen export destination is stored in `PawPrints/export-settings.json`. Save PNG prompts for a destination when needed; missing destinations cause a save error instead of being recreated. Legacy mod-local data is copied without overwriting profile data or changing installed files. This keeps Borea-managed mod content separate from user data.

## Preview updates and text

`LivePreviewControls.cs` schedules debounced settings updates only when enabled. `Rendering/PreviewUpdates.cs` selects cached appearance composition or geometry rebuilding. Generate PawPrint explicitly recaptures the craft. One active worker and cancellation keep old jobs from becoming accepted documents; UI rendering and texture uploads remain on the game thread.

`RichText.cs` and `FreeText.cs` store formatted ranges and lay out text. The editor edits those ranges in a single styled surface. `TextBoxPlacement.cs` retains resource validation while free layout permits overlap and off-page positions; legacy automatic layouts retain their fit checks. `AnnotationDocument` holds undo/redo snapshots of accepted composition changes.

`Storage/TitlePreferences.cs` and `Storage/ExportPreferences.cs` validate bounded settings files. `PrintDiagnostics.cs` records bounded, local action diagnostics. `PrintUiText.cs` keeps user content away from native printf-style format strings.

## Builds and acceptance

`tools/configure.py` writes ignored local prerequisite paths. `tools/build.py --stage` checks the inspected game hashes, builds and packages the runtime without launching or updating a game profile. `Directory.Build.props` and `Directory.Build.targets` supply and validate explicit game/loader paths.

`tests/Blueprint.Tests.csproj` is a console regression runner, not an xUnit project. The default runner covers portable synthetic suites; explicit --fixtures checks require separately provisioned game-derived data and fail clearly when it is absent. `tests/Blueprint.Acceptance.csproj` is a separate game-side driver; building it does not establish native acceptance.

Optional capture helpers live under `tools/capture/`; their output stays in ignored `tests/fixtures/`. They and their Python dependencies are not runtime components. Capture fixtures must be supplied locally and are excluded from version control.

## Maintenance boundaries

Use `.editorconfig` for C# formatting. Prefer one operation per line and named intermediate values when a long expression obscures a state transition. Explain native API assumptions and coordinate systems where they affect behavior.

Changes to shared editor state, interaction dispatch or render scheduling require focused history/cancellation regressions and native interaction checks. Worker tasks must not retain live game objects, and stale results must not replace newer accepted edits.
