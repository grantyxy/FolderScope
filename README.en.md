# FolderScope

**An open-source alternative to FolderSizes, independently rewritten in C# / .NET Framework, with Simplified Chinese and English interfaces.**

[简体中文](README.md) · [Downloads](https://github.com/grantyxy/FolderScope/releases) · [Build checks](https://github.com/grantyxy/FolderScope/actions/workflows/build.yml) · [MIT license](LICENSE)

FolderScope analyzes disk usage, finds large and duplicate files, generates reports, and visualizes storage distribution on Windows. Its workflow and layout are inspired by [FolderSizes](https://www.foldersizes.com/). The current implementation includes a UI and scanning engine rewritten and optimized with assistance from Claude.

This is an independent project with no affiliation to FolderSizes and no dependency on its installation, DLLs, or license. “Rewritten” describes the independent implementation; it does not imply complete feature, UI, or file-format parity.

## Run

Download `FolderScope-v1.0.0-preview.1-win-x64.zip` from [Releases](https://github.com/grantyxy/FolderScope/releases), extract it, and run `FolderScope.exe`. Keep `FolderScope.exe.config` and the help files beside it. Use the **中文 / EN** menu to change the interface language without discarding the current scan.

Targets Windows 10 / 11 x64 with .NET Framework 4.8. Local release validation was performed on Windows 11 x64. No Python, Node.js, server, or NuGet packages are required.

## Features

- Simplified Chinese / English UI, translated report headers, light and dark themes.
- Local NTFS MFT reading with parallel directory enumeration as a fallback; multiple roots, pause, cancellation, filters, and event logs.
- Bar, pie, sunburst, and treemap charts with folder navigation and PNG export.
- 17 reports covering large, old, new, temporary, duplicate and categorized files, empty folders, and long paths.
- Metadata-based duplicate detection and SHA-256 content comparison.
- Wildcard, regex and numeric search rules; metadata snapshots, comparisons, and disk history.
- XLSX, CSV, HTML, PDF, XML and TXT export, plus print preview.
- File operations, ZIP archives, CLI scans, Windows scheduled reports, share discovery and SMTP integration.

MFT access generally requires administrator privileges and a local NTFS volume. The application falls back to directory enumeration when MFT access is unavailable. MFT mode can read the entire volume's MFT even for a selected subfolder; performance depends on the workload. The status bar identifies the actual engine used. The local release self-test ran without elevation and therefore covered directory enumeration, not a full MFT validation.

## Build and test

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
$testDir = Join-Path $env:TEMP ('FolderScope-tests-' + [guid]::NewGuid().ToString('N'))
$process = Start-Process -FilePath .\FolderScope.exe -ArgumentList @('--self-test', ('"' + $testDir + '"')) -Wait -PassThru
Get-Content (Join-Path $testDir 'test-results.txt')
if ($process.ExitCode -ne 0) { throw 'FolderScope self-test failed' }
```

The build uses the Windows .NET Framework C# compiler and writes `FolderScope.exe` to the repository root. Self-tests create sample files and reports in the chosen test directory. All **29 / 29** checks passed locally during release preparation on 2026-10-23. GitHub Actions rebuilds and runs the same checks.

## Preview limitations

This initial preview does not guarantee parity with FolderSizes. Its `.fsscan` format is specific to FolderScope. Full docking, advanced condition groups, capacity prediction, and encrypted ZIP are not implemented. PDF pages are rasterized; use XLSX, CSV, or XML for full paths and selectable text.

A brief blank state when opening File Reporter was reported; this release has not completed reproduction or repair verification for that issue. Actual SMTP delivery, scheduled task triggers, network shares, large-disk performance, and complex NTFS/MFT cases need further validation. Size totals are not unique physical volume usage. Metadata duplicate matches do not prove identical content.

Settings and logs are stored in `%LOCALAPPDATA%\FolderScope`. Remove personal paths and credentials before sharing logs. See the bundled `UserGuide.html` for usage, command-line examples, and operational details.

## License

[MIT](LICENSE). FolderSizes is referenced to identify the project inspiration; its name and associated rights belong to their respective owners.
