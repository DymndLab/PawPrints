# Modded part support

PawPrints can draw the geometry of many modded parts without special rules. Its `part-support.json` supplies drawing surface treatments and fallback labels for parts that need them. These rules do not change a part's in-game appearance, power production or other behavior, and they do not guarantee compatibility with every mod's rendering features.

You can edit your own installed copy or contribute rules to PawPrints so other users receive them. No C# changes or recompilation are needed for a local rule edit. PawPrints reads one file beside its DLL; it does not discover separate rule files in other mods.

## Example: Tessera solar cells

Tessera's Solar Wall Panel has a parent part named `TesseraStructA_Prefab_SolarWallPanelA`. Its visible geometry belongs to separate frame and cell subparts. PawPrints chooses each mesh's surface from the template ID of the part or subpart that owns its model, so the cell rule must target `TesseraPanelA_Subpart_WallCells`, not the parent panel's name.

The wall and half-panel cell rules in the repository's [part-support.json](../part-support.json) are:

```json
[
  {
    "templateId": "TesseraPanelA_Subpart_WallCells",
    "surface": "solar"
  },
  {
    "templateId": "TesseraPanelA_Subpart_HalfCells",
    "surface": "solar"
  }
]
```

These are entries for the existing `rules` array, not a replacement for the whole file. If your installed version already has them, do not add duplicates.

The frame, cells, radiator and other panel surfaces share Tessera's material atlas. A rule for just the cell subparts preserves structural drawing treatment on the surrounding frame. The repository rules cover these two IDs from Tessera 1.1.1; their final appearance still needs in-game export verification before being called visually tested.

## A. Edit your local installation

1. Save any drawing you need as a PNG, then close KSA before editing installed files.
2. Find PawPrints in the profile you actually launch. In a Borea instance, the file is `<instance folder>/mods/PawPrints/part-support.json`, beside `PawPrints.dll`. Other StarMap profiles use their own `mods/PawPrints` folder. Editing a repository checkout or another profile will not change the installed mod.
3. Back up the existing file somewhere outside the managed mod folder.
4. Open it in a text editor. Add the needed objects to its `rules` array, keeping the existing rules, `version: 1` and `labelCategories`. Separate objects with commas. Save as UTF-8.
5. Launch the same profile, open your craft in PawPrints and choose **Generate PawPrint**. Check the affected surface and its neighboring frame, then export a PNG to verify the result at your intended resolution.

Each Generate PawPrint loads a new rules snapshot. Changing an existing drawing's appearance does not reload the rules. An invalid file rejects the new generation with a warning and retains the previous drawing; restore the backup or correct the reported problem. If the file is missing, PawPrints uses the defaults embedded in that installed DLL, which may be older than this repository.

Borea and other installers may replace `part-support.json` during updates or reinstalls. Keep your custom additions separately and merge them into the new file rather than replacing newer defaults with an old backup. Contributing them upstream avoids that repeated maintenance.

## Find the right template ID

For XML-based part packs, inspect the mod's asset and game-data XML files:

1. Search for the displayed part name, such as `Tessera Solar Wall Panel`, and note its `<Part Id="...">`.
2. Find that part's asset definition. Follow its `<SubPart ... Ref="...">` references, including nested subparts where present.
3. Locate the `<Part>` or `<SubPart>` whose `<Components>` contains the relevant `<PartModel>`. Use that owning element's **Id**, not the model's Key, mesh reference, material ID or numbered instance ID from the game's menu. Dynamic and glass models are also classified by their owning part's template ID.

For example, the relevant Tessera definition has this shape:

```xml
<SubPart Id="TesseraPanelA_Subpart_WallCells">
  <Components>
    <PartModel Key="TesseraPanelA_Subpart_WallCells_Model">
      <Mesh Ref="TesseraPanelA_Subpart_WallCells" />
      <Material Ref="TesseraPanelA_Material" />
    </PartModel>
  </Components>
</SubPart>
```

A display name containing "Solar" does not identify every child mesh as solar. If one model-owning part mixes cells and frame geometry, today's template rules cannot select individual faces within it. Ask the mod author which templates own the surfaces before applying a broad rule.

## Rule reference

Each rule has exactly one selector and at least one setting:

| Field | Meaning |
| --- | --- |
| `templateId` | Exact template ID. Prefer this for individual surfaces. |
| `templatePrefix` | Literal beginning of an ID. Use only for a family whose every match needs the same setting. |
| `templateContains` | Literal substring anywhere in an ID. Audit all matches carefully. |
| `surface` | `structure`, `solar` or `foil`. |
| `fallbackLabel` | A readable name used when a useful native display name is unavailable. |

Matching is case-sensitive. Selectors are not regular expressions or wildcards. For each setting independently, an exact ID wins over patterns; otherwise the first matching pattern in file order wins. A label-only rule does not prevent another matching rule from supplying a surface. Duplicate selectors are rejected: combine their settings into one object.

`structure` uses the ordinary geometry treatment and is the default. `solar` adds detail from the authored normal and colour textures and suppresses ordinary structural hatching on that surface. `foil` uses the authored material normals for the existing foil treatment. A rule cannot invent texture detail that the mod does not provide or make unavailable texture data readable.

Fallback labels are optional. A useful native display name takes precedence. Part-list labels usually describe the structural parent assembly, so target that assembly's ID when naming the whole part; surface rules may target its children separately. Preserve `labelCategories` unless you intentionally want to change which category prefixes appear in mechanically generated names.

## B. Contribute support to PawPrints

1. Fork the [PawPrints repository](https://github.com/DymndLab/PawPrints) and create a branch.
2. Add the smallest sufficient rules to the root `part-support.json`. Prefer exact IDs and preserve existing rules. This same file is packaged beside the DLL and embedded as its fallback during the build.
3. Add cases to `tests/PartSupportChecks.cs`: the intended surface must match, and nearby frames, skins or other lookalike IDs must keep their intended treatment. Include label behavior if your contribution changes labels.
4. Install the contributor prerequisites described in [DEVELOPMENT.md](../DEVELOPMENT.md), then run the focused portable checks from the repository root. These checks do not require the game:

   ```powershell
   dotnet run --project tests/Blueprint.Tests.csproj -c Release -- --part-support
   python tools/repository_checks.py
   ```

5. Test the proposed rules in your own installed copy using the local procedure above. Check both solar panel sizes or relevant variants, front/back/oblique views, surrounding structure, and your exported PNG. Report any material warnings. Automated classification checks do not prove visual correctness.
6. Open a pull request with the mod name, version and source link; the relevant template IDs; what was wrong; and what you tested. Include before/after images you have permission to share. Do not commit extracted meshes, texture atlases, game files, saves or private logos. An untested contribution is still useful: clearly mark in-game acceptance as pending.

If you do not want to make a pull request, [open an issue](https://github.com/DymndLab/PawPrints/issues) with the same information and your proposed rule objects. Rules merged into the repository reach players in a subsequent PawPrints release; they do not automatically modify already installed copies.

If a correctly matched surface remains blank, check the generation warnings and the mod's texture/UV data before broadening the selector or requesting renderer changes. Frames becoming textured or hatched differently usually indicate a rule that matches too much.
