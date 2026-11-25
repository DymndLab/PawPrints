# Installation

PawPrints **0.6.1** requires **Windows x64**, **KSA 2026.11.25.5949** and **StarMap 0.5.4**. In Borea, choose PawPrints in the Stable channel for an instance with those versions. Native Linux testing is pending.

## Install with StarMap

1. Close KSA.
2. Download `PawPrints-0.6.1.zip` from [Releases](https://github.com/DymndLab/PawPrints/releases). Use the mod ZIP; GitHub's source archives contain development files.
3. Extract its `PawPrints` folder into your StarMap profile's `mods` directory. Keep the packaged libraries and licenses together.
4. Enable `PawPrints` in that profile's `manifest.toml`, updating an existing entry if present:

   ```toml
   [[mods]]
   id = "PawPrints"
   enabled = true
   ```

5. Launch the profile through StarMap and load a craft in the Vehicle Editor.
6. Choose **Editor > Open PawPrint Editor** or press **Ctrl+Alt+P**.

Python, Node, the .NET SDK and Blender are not required to play. Contributors building from source should use [DEVELOPMENT.md](https://github.com/DymndLab/PawPrints/blob/main/DEVELOPMENT.md).

## Export folder and preferences

On first opening, choose where to save PNGs. PawPrints remembers the destination for that game profile. **Preferences > Change export folder...** changes it; **View exported drawings** opens it. Cancelling the folder prompt still allows editing. If the chosen folder becomes unavailable, saving displays a warning so you can choose another destination. Existing prints stay where they were saved.

Company logos must be PNG files; transparency and image proportions are preserved. Single black or white logos follow the paper theme. Coloured logos retain their colours.

Company logos and title defaults are stored under `<active profile>/PawPrints/Logos`. Your drawn-by name, initials, company and selected logo carry across sessions in that profile. You can edit the approver name and initials for the current session.

**Live preview updates** is off by default. When enabled in Preferences, menu changes update the drawing after a short pause or slider release. Otherwise, select **Generate PawPrint** to apply them. Placing and editing objects updates the drawing immediately. Generate PawPrint also recaptures the craft from the Vehicle Editor.

## Arrange a new drawing

On **New drawing**, set the sheet, choose **Paper & Wear**, and fill in **Drafter Details**. Type your initials and click a handwriting sample to choose its font. Click **Blank drawing** to open the editor, **Arrange views** for the grid, or **From template** for the gallery. **Open Drawing** and **Return to Vehicle Editor** are beside the New Drawing heading. In **Grid Size**, drag from **X** or click a cell to select up to five columns and four rows. The size beside the heading shows the current grid. Select **Automatic** to let PawPrints size the grid to fit the selected views. Click a circled **P** to choose the primary projection. Dashed guides connect views that share scale. Drag the primary to move its linked views together. Drag a secondary along its projection axis to keep it linked, or confirm an off-axis drop to give it independent scale. You can also click a view and then a destination cell. Open **Paper & wear** beside the grid to preview appearance, then choose **Start editing**.

**Save as Defaults** remembers the reusable settings for the active profile. Use **Export Preferences** or **Import Preferences** on New drawing to move a `.pawprofile` file between instances. It includes the default logo. Export can also include saved layouts and extra logo images. Import reviews the destination before applying and keeps existing library files.

## Editable projects and 4K export

Use **Save Drawing** to keep a `.pawprint` in the active profile's `PawPrints/Drawings` folder. To resume after a restart, load the matching vehicle and choose **Open Drawing**. A changed or ambiguous assembly can prevent reopening. In an open drawing, refresh after a craft edit and resolve missing exploded roots before exporting.

Choose **Save PNG** to export the current sheet. At 3840 x 2160 or 2160 x 3840, **Preferences > Detailed export** enables finer final linework. The preference starts off and is remembered across size changes. **Preview final PNG** shows the export pixels and encoded size. Set a PNG size target to review a smaller copy when the original exceeds it. The original remains available at its selected dimensions.

## Limitations

- Large craft and high-resolution drawings can use substantial memory.
- Third-party parts and display scaling configurations may need additional compatibility testing.

## Troubleshooting

PawPrints requires a loaded craft. An empty editor shows **No vehicle loaded** when hovering its menu entry.

The local diagnostic log is `<active profile>/logs/PawPrints.log`. It records recent actions, capture geometry counts and part template IDs, and error stacks without typed drawing text. Include reproduction steps and the exact PawPrints, KSA and StarMap versions when [reporting a bug](https://github.com/DymndLab/PawPrints/issues). Review [security and local data](https://github.com/DymndLab/PawPrints/blob/main/SECURITY.md) before sharing game logs or crash dumps.
