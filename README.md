# PawPrints

Create blueprint-style PNG drawings of your Kitten Space Agency craft inside the Vehicle Editor.

Arrange orthographic, projected, isometric and exploded views. Add part labels, flagnotes, dimensions, formatted text and performance blocks, then finish the sheet with a title, company logo and signoffs. Choose Blue, White, Cream or Charcoal paper with optional grids, zones and wear.

Part poses affect the drawing without changing your craft. Rendering runs locally; no Blender, AI service or companion application is required.

## Get started

**Release 0.5.39** targets **Windows x64**, **KSA 2026.11.16.5901** and **StarMap 0.5.4** and is available in Borea's Stable channel. See [5901 verification and remaining coverage](docs/ksa-5901-testing.md) for the tested scope. Other versions are unsupported. The editor workflow may change during development.

The source branch also contains [unreleased fixes and their remaining acceptance checks](docs/unreleased-fixes.md). Those changes are not included in the published 0.5.39 download.

Native Linux support is open to community contributions and testing. The current release uses Windows APIs for font rendering, PNG logo loading and file dialogs. There is no Linux release date; interested contributors and testers can [open or join a Linux support discussion in Issues](https://github.com/DymndLab/PawPrints/issues). Please discuss the approach before starting a substantial port.

1. Download the mod ZIP from [Releases](https://github.com/DymndLab/PawPrints/releases) and follow the [installation instructions](INSTALL.md).
2. Load a craft in the Vehicle Editor, then choose **Editor > Open PawPrint Editor** or press **Ctrl+Alt+P**.
3. Choose an export folder, arrange your drawing and select **Save PNG**.

Exports are images. Save PNG before loading another craft, leaving the Vehicle Editor or ending the game: editable sheets are not saved across those transitions. **Back to editor** keeps the sheet while the same craft stays open. Large craft and high-resolution sheets can use substantial memory; compatibility with third-party parts varies with their implementation.

For problems, [open an issue](https://github.com/DymndLab/PawPrints/issues) with reproduction steps and your PawPrints, KSA and StarMap versions. See [security and local data](SECURITY.md) before sharing logs or files.

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
