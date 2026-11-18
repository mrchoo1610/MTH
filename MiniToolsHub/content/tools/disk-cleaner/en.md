A full C: drive is a familiar headache, but "cleaner" tools tend to make people nervous about deleting something important. **Disk Cleaner** takes the opposite approach: rule number one is **never delete the wrong thing**.

### Scan first, delete later

Every cleanup starts with a **dry-run scan**: the app shows how many GB each item would free, without deleting anything. Files are removed only after you press **Clean** and confirm. The scan uses the very same filters as the real cleanup, so the preview matches the result.

### Safe by design

- **Whitelist**: only locations listed in `rules.json` are touched, such as `%TEMP%`, Windows Temp, Chrome/Edge caches, npm, NuGet, pip, Yarn and Gradle caches, crash dumps, thumbnail cache and the Recycle Bin.
- **SafetyGuard**: a hard protection layer that blocks `Windows`, `Program Files`, `ProgramData`, Documents, Desktop, Pictures, OneDrive, drive roots and your user folder, even if a rule points there by mistake.
- Only the contents **inside** a rule's folder are deleted, junctions and symlinks are **never followed**, and cloud-only OneDrive files are skipped.
- Locked or inaccessible files are skipped and logged instead of crashing the app.
- Each cleanup writes a log listing every deleted file.

### Four tabs

| Tab | What it does |
|---|---|
| Overview | Fixed-drive usage and a **Quick scan** for the default rules |
| Clean | Pick rules, dry-run, then clean, with progress, cancel and a results table |
| Analyzer | A size-sorted folder tree to find what is filling the drive |
| Large files | Finds the biggest files and only moves them to the Recycle Bin (restorable) |

### Custom rules

Got a cache folder that takes tens of GB but is not in the list (CapCut, video editors, game launchers...)? Open the Analyzer, select the folder and press **Add to Clean**. A dedicated safety policy refuses dangerous places such as `AppData`, `Downloads` or the program's own folder, and warns when the folder name does not look like a cache.

### Administrator rights only when needed

The app runs as a normal user. Only a few system rules (Windows Temp, Windows Update cache) ask for UAC, and the elevated process accepts just a list of known rule IDs, never trusting paths from outside.

### Quick start

1. Download the ZIP and extract it.
2. Run `DiskCleaner.exe`. No installation needed.
3. Go to **Overview**, press **Quick scan**, then open **Clean** to review and confirm.
