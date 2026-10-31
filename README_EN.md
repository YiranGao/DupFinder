# DupFinder — Find and Clean Duplicate-Named Files and Folders

[中文](README.md) | English

<img src="assets/icon_preview.png" width="96" height="96" align="right" alt="DupFinder icon">

DupFinder scans a folder, groups together the files and folders that **share the same name**, and lets you delete the ones you select — straight to the Recycle Bin, so everything stays recoverable.

## 1. Getting it

- **Option A (recommended)**: download `DupFinder.exe` (~200 KB, embeds 9 icon sizes from 16 to 256) from **Releases** and double-click it.
- **Option B**: build from source — install .NET SDK 6.0+ and run `build.bat`. Output lands in `dist\DupFinder.exe`.

Runtime requirements: only the .NET Framework 4.0+ that already ships with Windows 10. **No .NET Runtime install, no third-party libraries, one single file.** You can also drag a folder onto the window to set the scan target.

## 2. What counts as a "duplicate name"

Names are compared **without their extension**, case-insensitively, across the whole tree:

```
D:\data\aaa\            <- folder
D:\data\aaa.txt
D:\data\sub\aaa.zip
D:\data\sub\deep\aaa.exe
```

All four land in one group named `aaa`. Names appearing only once are not listed (a lone `bbb.txt` is not a duplicate).

Turn off **"Ignore extension"** if you want a full-filename match instead — then `aaa.txt` and `aaa.zip` are no longer considered duplicates.

## 3. Using the GUI

1. **Scan folder**: type a path, click **Browse...**, or drag a folder onto the window
2. **Options**
   - Recursive scan (on by default)
   - Ignore extension when matching (on by default — the rule above)
   - Include files / Include folders (either one can be turned off)
   - Skip system directories (on: Recycle Bin, System Volume Information, `.git`, `.svn`, ...)
   - Compute folder size (off by default; enabling it makes scanning noticeably slower)
   - Minimum group size (2 by default; raise it to 3 or 4 to see only the worst cases)
3. **Pick what to clean**
   - Tick a single item, or tick a group name to select/clear the whole group
   - **Smart select**: keep *newest / oldest / largest / shortest path / nothing* per group and tick everything else
   - Select all / clear all / invert
4. **Delete**: click **Delete selected to Recycle Bin**. A confirmation dialog lists what will be removed plus the total size, and warns in red when folders are involved — a folder is moved to the Recycle Bin together with everything inside it. Everything remains restorable from the Recycle Bin.
5. Double-click an item to reveal it in Explorer; right-click for copy path / untick / remove from list; **Export list** writes a CSV.

## 4. Safety

- Deletion only goes through the Windows Shell Recycle Bin interface — never a permanent delete
- Every path is validated before deletion; the following are blocked and reported: drive roots, the scanned root itself, protected locations (Windows, System, Program Files, user profile, Desktop, Documents), paths outside the scanned folder, and paths longer than 240 characters
- Reparse points (symlinks, junctions, shortcuts) are skipped so nothing outside the tree can be removed
- Directories without access permission are recorded as warnings; the scan keeps going

## 5. Command line (optional, for batch work)

```bat
DupFinder.exe --cli --dir "D:\data"                    :: list duplicate-name groups
DupFinder.exe --cli --dir "D:\data" --no-recursive     :: top level only
DupFinder.exe --cli --dir "D:\data" --with-extension   :: match full filename
DupFinder.exe --cli --dir "D:\data" --files-only       :: files only
DupFinder.exe --cli --dir "D:\data" --min 3            :: only groups with 3+ items
DupFinder.exe --cli --dir "D:\data" --folder-size      :: also compute folder sizes
DupFinder.exe --cli --dir "D:\data" --json             :: JSON output
DupFinder.exe --cli --dir "D:\data" --delete --yes     :: keep newest per group, recycle the rest
```

> `--delete` does nothing unless `--yes` is passed as well.

## 6. Building from source

Requires .NET SDK 6.0+ at build time only (it does not affect what the produced exe needs at run time):

```bat
build.bat
```

or manually: `dotnet build -c Release` → `bin\Release\net48\DupFinder.exe`.

> The `Microsoft.NETFramework.ReferenceAssemblies` package only supplies .NET Framework 4.8 reference assemblies while compiling; it is never bundled into the exe.

## 7. High-DPI and multi-monitor support

Verified on a 4K display at 150% scaling as well as mixed setups (primary at 100%, secondary at 150%):

- Per-monitor DPI awareness is enabled at startup, so Windows never bitmap-stretches the window (which would blur text and push the window off-screen)
- Window size is derived from the **screen the window is actually on**, using that monitor's scaling factor, so it never spills past the screen edge
- The options bar and the button bar are flow layouts that wrap automatically — narrowing the window adds rows instead of clipping controls

Troubleshooting (off by default, no impact on normal use):

```bat
set DUPFINDER_DEBUG=1
DupFinder.exe
```

writes the real bounds of every region to `%TEMP%\dupfinder_layout.txt`; `DUPFINDER_SHOT=1` additionally writes a window screenshot to `%TEMP%\dupfinder_shot.png`.

## 8. Source layout

```
src/
  Models.cs     Models (item / group / scan options / result)
  Scanner.cs    Tree walk and duplicate-name grouping (cancellation, progress, error collection)
  RecycleBin.cs Shell API delete-to-Recycle-Bin
  Safety.cs     Pre-delete path validation + formatting helpers
  NativeDpi.cs  Process DPI awareness and scaling factor
  Cli.cs        Command line mode
  MainForm.cs   WinForms UI
  Program.cs    Entry point
```

```
tools/
  gen_icon.py   Generates assets/DupFinder.ico (standard library only, see section 9)
```

## 9. Application icon

- Icon file: `assets/DupFinder.ico`, with 9 sizes (16 / 20 / 24 / 32 / 40 / 48 / 64 / 128 / 256), 32bpp with alpha.
- Regenerate it with `python tools/gen_icon.py` — a pure standard-library script (zlib + struct, SDF + supersampling), **no Pillow or any third-party package**. Rerun it and rebuild to pick up changes.
- The icon is embedded as a Win32 resource via `ApplicationIcon`, so Explorer, the taskbar and Alt-Tab all show it. A managed copy is embedded too, so the form can pick the right size at runtime (48x48 at 150% scaling — no blurry upscale).

## 10. License

MIT License — see [LICENSE](LICENSE).
