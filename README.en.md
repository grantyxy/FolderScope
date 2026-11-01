# FolderScope

**A fast disk space analyzer for Windows — see exactly what is filling your drives.**

[![Build and self-test](https://github.com/grantyxy/FolderScope/actions/workflows/build.yml/badge.svg)](https://github.com/grantyxy/FolderScope/actions/workflows/build.yml)
[![Latest release](https://img.shields.io/github/v/release/grantyxy/FolderScope?label=download)](https://github.com/grantyxy/FolderScope/releases/latest)
[![MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

[简体中文](README.md)

FolderScope scans drives or selected folders, ranks folders and files by size, visualizes the result as a treemap, sunburst and other charts, and ships 17 file reports for large, old, duplicate and temporary files. The interface is available in English and Simplified Chinese and can be switched at any time.

![FolderScope folder map](docs/images/treemap-en.png)

## Highlights

- **Two scan engines** — batched directory enumeration and direct NTFS Master File Table (MFT) reading. Invalid or unsupported MFT data falls back to enumeration. Performance depends on the storage device, directory size and scan options.
- **Clear visuals** — details grid, bar chart, donut chart, sunburst and folder map. Double-click any region to drill into that folder.
- **Find what to clean up** — 17 reports including largest files, oldest files, temporary files, empty folders, long paths and duplicate files (with optional SHA-256 content comparison).
- **Familiar workflow** — Office-style ribbon, Explorer file icons and a context menu to open, locate, copy, move, recycle or zip items.
- **Track changes** — save scan snapshots, browse them offline and compare two scans to see which folders grew.
- **Export and automate** — XLSX, CSV, HTML, PDF, XML and TXT reports, command-line scans, and scheduled reports with e-mail delivery through Windows Task Scheduler.

![Details view](docs/images/details-en.png)

## New in 1.1

- Large XLSX reports split automatically after 1,048,575 data rows per sheet, repeating headers, frozen first rows and filters.
- **Options → Scanning → Skip cloud placeholders** excludes cloud/offline files and directories. This option uses directory enumeration to inspect recall flags. Content duplicate checks skip cloud, reparse and incomplete entries, with visible counts and events.
- **Tools → Disk History → Capacity forecast** shows daily growth, fit and projected exhaustion of available space. It requires three distinct samples spanning at least 24 hours from the same volume within the last 30 days. Volume/capacity changes reset the sample segment; legacy samples without volume identity are excluded. Projections include available-space/quota changes and are not guarantees.
- Content duplicate checks retain size grouping, sample the first/last 64 KiB of files larger than 128 KiB, then fully hash remaining candidates with SHA-256. Samples never establish duplicates. Changed or unreadable candidates produce an error.
- Fixed wildcard exclusions, worker settings, MFT record/reference validation, concurrent name/parent publication, prefetch-buffer lifetime, and classification drill-down scope.

See the [comparison record](docs/foldersizes-comparison.md) and [FolderSizes release notes](https://www.foldersizes.com/content/static/releasenotes.htm). This is an independent implementation, without a claim of complete feature or algorithm parity.

## Download and run

1. Download `FolderScope-v1.1.1-win-x64.zip` from [Releases](https://github.com/grantyxy/FolderScope/releases/latest).
2. Extract it anywhere and run `FolderScope.exe`. Keep `FolderScope.exe.config` next to it.
3. Pick a drive or folder to scan. Use **中文 / EN** in the top-right corner to change the language.

**Requirements:** Windows 10 / 11 (x64) with .NET Framework 4.8. Extract and run; scheduled tasks are registered only when explicitly created in the app.

## Scan engines

| Engine | Used when | Notes |
| --- | --- | --- |
| NTFS MFT direct read | Running as administrator on a local NTFS drive | Reads the MFT and rebuilds the tree; unsupported or inconsistent records fall back to enumeration. Small subfolders may be slower |
| Parallel directory enumeration | Everything else (standard user, network paths, exFAT / FAT32, …) | Multi-threaded traversal, no elevation required |

The engine is chosen automatically and the status bar shows which one was used and how long the scan took. Use **Tools → Run as Admin** to enable MFT mode, or turn it off under **Options → Scanning**.

## Command line

```text
FolderScope.exe --scan "D:\Data" --snapshot "D:\Reports\scan.fsscan" --export "D:\Reports\details.xlsx"
FolderScope.exe --scan "D:\Data|E:\Data" --report "Largest Files" --export "D:\Reports\large.csv"
FolderScope.exe --scan "D:\Data" --report "Duplicate Files" --duplicate-mode "Content (SHA-256)" --export "D:\Reports\duplicates.html"
FolderScope.exe --benchmark C:\
```

- Separate multiple paths with `|`. Report names are English identifiers such as `Largest Files`, `File Types` and `Duplicate Files`.
- Exit codes: `0` finished, `1` error, `3` cancelled. Skipped or inaccessible items are listed in an adjacent `.events.txt` file.
- `--benchmark` scans the path with both engines and writes a timing and result comparison.

## Build from source

Only the .NET Framework compiler that ships with Windows is needed — no Visual Studio or NuGet:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The build writes `FolderScope.exe` to the repository root; sources live in `src/`. Run the built-in self-test with:

```powershell
$dir = Join-Path $env:TEMP 'FolderScope-tests'
Start-Process .\FolderScope.exe -ArgumentList '--self-test', "`"$dir`"" -Wait
Get-Content (Join-Path $dir 'test-results.txt')
```

## Notes

- Settings, disk history and logs are stored in `%LOCALAPPDATA%\FolderScope`; delete that folder to reset.
- Size totals count paths rather than unique hard-linked files and are not equivalent to physical volume usage. Compression, sparse files, reparse points and file-system metadata affect interpretation.
- The path index does not support names differing only by case. Detected collisions are logged and marked as partial scans; they must not be treated as complete totals.
- This release tests MFT parsing and fallback conditions with synthetic records. Real elevated whole-volume MFT scans, cloud-provider integration, network shares, SMTP delivery and scheduled triggers were not validated in this release cycle.
- Snapshots (`.fsscan`) use FolderScope's own format. PDF exports are rendered as images; export XLSX or CSV when you need full, selectable paths.
- Delete moves items to the Recycle Bin. Before files are moved or renamed, FolderScope checks that they have not changed since the scan.

## License

[MIT](LICENSE)
