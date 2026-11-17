# Drawing layout and interaction

## Document model

`DrawingPlacement` stores the shared physical scale and the main views. Each view has a stable ID, camera basis, normalized page centre and optional parent/alignment link. Camera direction is independent of identity, so a sheet can contain repeated directions. The limit is twelve main views.

`AnnotationDocument` owns accepted composition and undo/redo snapshots. Placement previews, pending text and drag gestures remain outside history until accepted. Regeneration rebinds compatible notes and details to a fresh craft capture. Missing targets or invalid retained content leave the previous print intact. PNG export saves pixels, not an editable document.

## Views and projection

**Place > Primary View** starts a main drawing. The initial scale determines all main views; exploded details use their own scale. **Secondary View** or **Project view...** creates a view linked to a source. The projection convention is selected under **Preferences > Projection**.

`DrawingPlacements.Roll` unfolds the camera along the source's horizontal or vertical axes. Third-angle places each face on the side from which it is seen; first-angle reverses that mapping. Chained views retain their source's rotation. Diagonal gestures produce corner views, with hysteresis at the switching boundary to avoid repeated renders. Independent isometric views have no parent alignment lock.

Moving a source translates its linked descendants and their annotations. **Unlock alignment** detaches a projected view. Deleting a view removes its attached notes, detaches its direct children and leaves those children on the sheet. Accepted movement and deletion support undo/redo.

**Adjust Scale** changes all main views about their accepted centres, or only the part size inside an exploded detail. The fit limit includes captions, other views, panels and the title block. The slider previews the limit before rendering; a value beyond it retains the last valid drawing.

## Page geometry

`PageLayout` supplies the frame, title bounds and placement limits. The inner frame has a 60-pixel margin in working-image coordinates. Footprints include captions and can touch the printed frame and title rules. They must stay clear of the title block and other blocking items.

Portrait and square title blocks span the page width. Landscape and wide desktop titles are anchored at bottom-right, sized to their content; manual placement can use the remaining lower-left space. Automatic template fitting uses the rectangular area above the title. The title's top rule aligns with the A/B zone boundary. Four equal signoff rows retain their typography while branding absorbs height differences between page shapes.

Changing orientation or ratio maps existing centres to the new page and fits the view group. Geometry can shrink to fit while caption height and capture padding remain fixed, so centre spacing is solved separately. View IDs and projection relationships survive the change.

## Snapping and collision rules

**Preferences > Snapping** controls nearby caption alignment and frame/table/text-box edge snapping. Tolerance is converted from screen pixels to paper pixels so zoom does not change its apparent reach. A snap must still satisfy page and collision checks.

Text boxes use their full stored rectangle for snapping and collision checks, even when **Outline** is off. Their width and height can change while retaining room for wrapped text. Labels allow manual crossings and independent leader endpoints. Specification leaders use obstacle-aware routes with at most two bends and a horizontal connection at the block.

Exploded details keep whole craft parts together by default. Their frame size changes group spread; part scale changes geometry size without moving group centres. Explicit component expansion can separate a part's own meshes. Detail sampling renders the projected crop at 3x, 2x or native resolution within a four-million-sample budget, then resolves to sheet pixels and picking IDs.

## Zones, grid and export size

`SheetZones` assigns five zones to the shorter edge and scales the longer edge proportionally. Numbers increase right to left; letters increase bottom to top. Ten small grid cells fill each zone. Turning **Zoned sheet** off hides the zone marks while preserving grid spacing and drawing limits.

**Layout > PC Desktop** supports presets and custom dimensions: 640-8192 pixels per side, at most 16,777,216 pixels and aspect ratios between 1:4 and 4:1. Rendering uses a larger working image when needed for legibility; export resamples to the selected dimensions. Scale metadata assumes the image fits on 8 x 11-inch paper, rotated for landscape. It does not use monitor dimensions.

## Interaction and checks

Right-click cancels an active placement; click blank paper to deselect. Delete removes the selected or hovered object. Ctrl+Z/Ctrl+Y undo and redo accepted edits. Escape is not a PawPrints shortcut. The rich-text editor supports selected-range styles and sizes, clipboard operations and its own edit history.

Run `dotnet run --project tests/Blueprint.Tests.csproj -c Release -- --drawing-placement` for projection, scale, collision, history, repaging and export-size regressions. Other focused suites cover borders, panels, text and exploded details; see [DEVELOPMENT.md](../DEVELOPMENT.md). `python tools/acceptance.py --drawing` exercises the real runtime in a disposable StarMap profile while KSA is closed.
