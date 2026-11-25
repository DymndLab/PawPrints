# PawPrints

Create blueprint-style PNG drawings of your Kitten Space Agency craft inside the Vehicle Editor.

Arrange orthographic, projected, isometric and exploded views. Add part labels, flagnotes, dimensions, formatted text and performance blocks, then finish the sheet with a title, company logo and signoffs. Choose Blue, White, Cream or Charcoal paper with optional grids, zones and wear.

Part poses affect the drawing without changing your craft. Rendering runs locally; no Blender, AI service or companion application is required.

## Get started

**PawPrints 0.6.0** requires **Windows x64**, **KSA 2026.11.25.5949** and **StarMap 0.5.4**. Install it from Borea's Stable channel or use the release ZIP.

Native Linux testing is pending. If you can test on a native Linux KSA and StarMap setup, [open or join a Linux testing discussion](https://github.com/DymndLab/PawPrints/issues).

1. Install through Borea or download the mod ZIP from [Releases](https://github.com/DymndLab/PawPrints/releases) and follow the [installation instructions](INSTALL.md).
2. Load a craft in the Vehicle Editor, then choose **Editor > Open PawPrint Editor** or press **Ctrl+Alt+P**.
3. Choose an export folder, arrange your drawing and select **Save PNG**.

Use **Save Drawing** to keep an editable project for the current craft. Large craft and high-resolution sheets can use substantial memory. Compatibility with third-party parts varies with their implementation.

For problems, [open an issue](https://github.com/DymndLab/PawPrints/issues) with reproduction steps and your PawPrints, KSA and StarMap versions. See [security and local data](SECURITY.md) before sharing logs or files.

## What’s new in 0.6.0

Start with a blank drawing, a template, or **Arrange views** to drag views into a snapping grid before editing. **Import Preferences** and **Export Preferences** transfer defaults and optional libraries between instances. Linked projections share a scale, and independent views can overlap. **Save Drawing** and **Open Drawing** keep editable projects in the active profile. **Save PNG** exports the finished image. At 4K, Preferences offers an optional Detailed export and a final preview with its exact encoded size. Archival wear adds lettering rubs, paper tears and occasional coffee rings. Choose **Place > Place wear** to add an individual mark. [Release notes](distribution/RELEASE-NOTES.md)

## Contributing

For missing solar-cell detail, foil treatment or fallback labels on modded parts, see [Modded part support](docs/modded-part-support.md). The guide covers editing your installed rules and contributing support to PawPrints, using Tessera's solar panels as an example.

[DEVELOPMENT.md](DEVELOPMENT.md) covers building, tests, packaging and part-support definitions. The [architecture map](docs/architecture.md) explains the code.

| Directory | Contents |
| --- | --- |
| `src/` | Game integration, editor controls, rendering and storage |
| `tests/` | Automated checks, manual checklist and native game test drivers |
| `tools/` | Build, packaging, audit and capture tools |
| `assets/fonts/` | Initials fonts, licenses and provenance |
| `docs/` | Architecture, drawing layout and performance calculations |
| `distribution/` | Package metadata and release notes |

Project code is [MIT licensed](LICENSE). Fonts and dependencies retain their [own licenses](THIRD-PARTY-NOTICES.txt). Game assets and assemblies are loaded from your KSA installation and are not included in the mod.
