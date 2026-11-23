# Installation

PawPrints **0.5.39** requires **Windows x64**, **KSA 2026.11.16.5901** and **StarMap 0.5.4**. It refuses unsupported game builds and is available in Borea's Stable channel.

PawPrints remains under active development. Native drawing, performance and pose scenarios and focused editor checks have passed on 5901.

## Install with StarMap

1. Close KSA.
2. Download `PawPrints-0.5.39.zip` from [Releases](https://github.com/DymndLab/PawPrints/releases). Use the mod ZIP; GitHub's source archives contain development files.
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

Company logos and title defaults are stored under `<active profile>/PawPrints/Logos`. Your drawn-by name, initials, company and selected logo carry across sessions in that profile. Approver fields start each session as `Dymnd H. K.` and `DHK` and can be edited for that session.

**Live preview updates** is off by default. When enabled in Preferences, menu changes update the drawing after a short pause or slider release. Otherwise, select **Generate PawPrint** to apply them. Placing and editing objects updates the drawing immediately. Generate PawPrint also recaptures the craft from the Vehicle Editor.

## Preview limitations

- Exports are PNG images. Save PNG before loading another craft, leaving the Vehicle Editor or ending the game. Editable sheets do not persist across those transitions. **Back to editor** keeps the sheet while the same craft stays open.
- Large craft and high-resolution drawings can use substantial memory.
- Third-party parts and display scaling configurations may need additional compatibility testing.

## Troubleshooting

PawPrints requires a loaded craft. An empty editor shows **No vehicle loaded** when hovering its menu entry.

The local diagnostic log is `<active profile>/logs/PawPrints.log`. It records recent actions, capture geometry counts and part template IDs, and error stacks without typed drawing text. Include reproduction steps and the exact PawPrints, KSA and StarMap versions when [reporting a bug](https://github.com/DymndLab/PawPrints/issues). Review [security and local data](https://github.com/DymndLab/PawPrints/blob/main/SECURITY.md) before sharing game logs or crash dumps.
