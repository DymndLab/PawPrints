# PawPrints 0.5.39 preview

Updated for KSA 5901: engine ratings, main-nozzle gimbal selection and propellant labels now use the game's rocket/core API. The exact game-build guard remains enforced. Drawing, performance and pose scenarios plus focused editor and Borea-profile export checks passed on the supported build. See [verification and remaining coverage](../docs/ksa-5901-testing.md).

The print workspace keeps native editor resources and staging panels out of the drawing, restores focus after resizing, and yields to native settings and save/load dialogs.

Company logos accept PNG files with transparency. Windows handles PNG decoding; the mod includes its own image resampling and PNG export.

Create blueprint-style PNG drawings of your KSA craft in the Vehicle Editor.

- Arrange orthographic, projected, isometric and exploded views.
- Add part labels, flagnotes, dimensions, formatted text and performance blocks.
- Customize the title block, company logo, signoffs and revision.
- Choose Blue, White, Cream or Charcoal paper, with optional grids, zones and wear.
- Pose moving parts in the drawing without changing the live craft.
- Add kitten and banana scale references.
- Remember export-folder and company preferences between sessions.

Load a craft, then use **Editor > Open PawPrint Editor** or **Ctrl+Alt+P**.

Requires Windows x64, KSA **2026.11.16.5901** and StarMap **0.5.4**. Rendering runs locally without Blender, an AI API or a companion application.

This is a development preview. Save PNG before loading another craft, leaving the Vehicle Editor or ending the game: editable sheets are not saved across those transitions. Back to editor keeps the sheet while the same craft stays open. Large craft and high-resolution drawings can use substantial memory. Third-party part compatibility and display scaling may vary.

See [installation instructions](../INSTALL.md) and [report issues](https://github.com/DymndLab/PawPrints/issues) with reproduction steps and your game/mod versions.
