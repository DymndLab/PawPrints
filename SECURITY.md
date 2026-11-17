# Security and local data

PawPrints renders locally inside the KSA process. It opens no server, uses no AI API, performs no telemetry upload and contains no auto-updater. Installing a code mod still grants that code the permissions of the game process; it is not sandboxed.

## Files and inputs

- Capture reads the loaded craft on the editor thread. Rendering operates on detached copies with cancellation and geometry/material budgets.
- PNG logos are decoded only after file-size, dimensions, pixel-count, format and animation checks. Imports use a generated name in the active profile's logo library; they do not overwrite unrelated files.
- Initials fonts are bundled, privately loaded for rendering and never installed system-wide. Their exact unmodified files and OFL licenses are recorded in `assets/fonts/source.json`.
- PNG exports go only to the user-selected folder. Saves use new temporary files followed by an atomic rename and do not overwrite an existing print. A missing destination produces an error instead of silently changing paths.
- Title defaults, logo selection and the live-preview choice stay in the active profile. Approval fields reset to their defaults on each session. Mod installation files are separate from writable user data, including in Borea instances.
- Local logs live at `<active profile>/logs/PawPrints.log`, with three rotated files and a 512 KiB limit per file. The custom log omits typed drawing text, exception messages and personal paths. KSA/loader logs or Windows crash dumps can still include machine-specific data; inspect and redact them before sharing.

## Release checks

`tools/repository_checks.py` verifies ignore coverage. `tools/release_audit.py --staged` scans the exact next commit, reachable history and packaged files for common secrets, local-account paths and disallowed artifacts. It separately warns about non-noreply email addresses in commit metadata without printing the addresses. Those warnings require a maintainer privacy decision; they are not credential findings. NuGet/npm advisory checks supplement source review. These checks cannot guarantee the absence of unknown vulnerabilities, native-library defects or third-party mod conflicts.

For a suspected vulnerability, use GitHub's **Report a vulnerability** button on the [Security page](https://github.com/DymndLab/PawPrints/security), when available. This opens a private report to the maintainers. Include the affected versions, reproduction steps, expected impact and a minimal synthetic example. Do not attach credentials, personal saves or unredacted memory dumps.

If private reporting is unavailable, open an issue titled **Private security contact requested** with no vulnerability details or attachments and ask the maintainer to enable private reporting. Wait for the private route before sharing details. Ordinary bugs can use the repository issue tracker with reproduction steps and the exact KSA/StarMap/PawPrints versions.
