# PawPrints 0.6.0 for KSA 5949

Supports Windows x64 with KSA 2026.11.25.5949 and StarMap 0.5.4. Native Linux testing is pending.

- Start with a blank sheet, a template, or a drag-and-drop grid with linked first- or third-angle projections.
- Save and reopen editable drawings, and import or export preferences between profiles.
- Re-root exploded views around a subassembly. Place a kitten or banana at a drawing view's scale and orientation.
- Add lettering rubs, tears and coffee rings to the sheet. Preview the final 4K PNG and check its file size before saving.

Load a craft in the Vehicle Editor and choose **Editor → Open PawPrint Editor**, or press **Ctrl+Alt+P**.

# PawPrints 0.6.0-local.50 preview

- New drawings and Preferences use the same default approver name and initials.

# PawPrints 0.6.0-local.49 shared preview

- PawPrints retains five rotated diagnostic logs alongside the current log.

# PawPrints 0.6.0-local.48 shared preview

- Arrange Views uses tapered dotted projection guides, with the wide end at the projected view.
- Activity is at the bottom of Preferences, with buttons to open the current PawPrints log or the active profile?s logs folder.

# PawPrints 0.6.0-local.47 shared preview

- Vehicle Editor transform prompts are hidden while PawPrints is open and resume when you return to the vehicle.

# PawPrints 0.6.0-local.46 shared preview

- **Kitten for Scale** and **Banana for Scale** deselect after each placement. Click the tool again to place another reference.

# PawPrints 0.6.0-local.45 shared preview

- **Place in view** lets a kitten or banana match a drawing view's scale and orientation. Manual orientation remains available.
- Click a dimmed branch beyond a rerooted subassembly's cut to restore that branch.
- The New Drawing divider groups the preference buttons with the settings.

# PawPrints 0.6.0-local.44 shared preview

- Starter and saved templates wrap into up to four columns as the window gets wider.
- In **Place > Exploded View**, right-click a part and choose **Set as root** to start with that subassembly. Use **Include all** to bring the rest of the craft back into scope.

# PawPrints 0.6.0-local.43 shared preview

- The selected handwriting sample uses a single blue outline. The six bundled fonts render with smoother strokes at the current button size.
- Hovering a starting card displays **Click to continue**.

# PawPrints 0.6.0-local.41 shared preview

- The illustrated starting cards open their routes with one click. **Open Drawing** and **Return to Vehicle Editor** are beside the **New Drawing** heading.

# PawPrints 0.6.0-local.40 shared preview

- New drawing gives **Paper & Wear** its own column. In **Drafter Details**, six rendered samples let you pick the handwriting for your initials.

# PawPrints 0.6.0-local.39 shared preview

- Arrange views uses paired dashed projection guides and highlights destinations that preserve a linked view. Hovering a primary button shows its own help and dismisses it when the mouse leaves.

# PawPrints 0.6.0-local.38 shared preview

- New drawing puts **Sheet Setup** and **Drafter Details** side by side, with three illustrated starting options below the preferences actions. Select an option, then click **New Drawing** to begin or **Open Drawing** to open an existing project.

# PawPrints 0.6.0-local.37 shared preview

- New drawing starts with Blank drawing, Arrange views or From template. Set the sheet, projection, drawing title, author and company before choosing a route. Arrange views includes the paper preview and opens the editor directly.
- **Save as defaults** stores reusable settings in the current profile. **Profile** imports and exports defaults, preferences and the selected logo. Saved layouts and extra logos are optional.

# PawPrints 0.6.0-local.36 shared preview

For KSA 2026.11.25.5949 and StarMap 0.5.4. Use the same ZIP for Windows x64 and native Linux x86-64 testing.

- In **New drawing > Select Views**, choose the sheet size, grid and projection convention. A filled circled P identifies the primary. Drag the primary to move its linked group, or drag an independent view onto another independent view to swap them.
- Dragging a secondary off its projection axis asks for confirmation before giving it independent scale. The cells you choose carry through Appearance into the editor, templates and saved drawings.
- Grid cells show a simplified sketch of the current craft. Larger craft use view symbols during setup. The editor and export render the craft geometry.
- The title block shows the projection convention and general decimal and angular tolerances.

# PawPrints 0.6.0-local.35 shared preview

For KSA 2026.11.25.5949 and StarMap 0.5.4. Windows x64 and native Linux x86-64 testers use the same ZIP. Native Linux acceptance is pending.

- Archival wear levels 1 through 5 always rub the lettering. Higher levels increase the count and strength. At level 5 every drawing gets an edge tear, 40% get two and 20% get a coffee ring.
- Open **Place > Place wear** to pick a thumbnail, adjust size and angle, and click to place. Tears support varied depth, mirroring and optional edge snapping. Right-click to finish or leave the Place tab.
- Placed marks can be added with automatic wear Off. Save Drawing preserves the wear layout, and PNG export includes it over the linework and lettering.

---

# PawPrints 0.6.0 Windows candidate

Build `0.6.0-local.34` targets KSA 2026.11.25.5949 and StarMap 0.5.4.

- Start from a blank sheet or template. Place linked projections at a common scale, arrange independent views and exploded details, and save the editable drawing in the active profile.
- Refresh after a vehicle edit. Surviving part references can follow the vehicle, and Activity identifies references that need attention. Restarting an editable project requires a matching assembly.
- Normal uses simpler linework for small meshes in large views. A focused assembly can retain full linework. Capture and rendering check geometry, image and material memory budgets.
- Export in landscape or portrait 4K. Preferences offers Detailed export, a final pixel preview, measured file sizes and a smaller PNG copy for a chosen size target. Detailed and Normal can look similar at page scale.

The Windows candidate uses a build-specific game assembly check. Craft complexity, layout and available memory affect which drawings it can export. Native Linux acceptance is pending.

---

# PawPrints 0.5.40 candidate

Local testing is underway. This is not a publication announcement.

- Cleaner curved-part linework and solar sketch detail, including Tessera solar cells.
- Fuel hoses included in drawings.
- Drawing recovery controls, dimension selection/deletion, clearer shared-scale limits, and editor interaction fixes.
- Editable performance-block vehicle headings and updated exploded assembly-guide preferences.
- Capture diagnostics and shared source mesh storage for repeated components.

Large stations that exceed one million captured vertices remain unsupported. Full large-craft export and broader workflow changes are tracked in the [roadmap](../docs/roadmap.md). Review [remaining release checks](../docs/release-0.5.40-testing.md) before promoting this candidate.

Requires Windows x64, KSA **2026.11.16.5901** and StarMap **0.5.4**.

---

# PawPrints 0.5.39 preview

Updated for KSA 5901: engine ratings, main-nozzle gimbal selection and propellant labels now use the game's rocket/core API. The exact game-build guard remains enforced. Drawing, performance and pose scenarios plus focused editor and Borea-profile export checks passed on the supported build.

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
