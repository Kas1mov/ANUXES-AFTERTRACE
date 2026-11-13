<div align="center">

# ANUXES AFTERTRACE

### A lightweight file inspection and change tracking assistant for Windows

**Inspect. Compare. Understand.**

![Version](https://img.shields.io/badge/version-0.2.0-9B75FF?style=for-the-badge)
![Platform](https://img.shields.io/badge/platform-Windows-263A63?style=for-the-badge&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)
![UI](https://img.shields.io/badge/UI-WPF-17151F?style=for-the-badge)

**Publisher:** ANUXES DIVISION · **Developer:** Kas1mov

<img width="1231" height="809" alt="ANUXES AFTERTRACE interface" src="https://github.com/user-attachments/assets/ee3ccbc7-9ae9-41a0-9f36-45f13c4ab1d2" />

</div>

---

## Overview

**ANUXES AFTERTRACE** is a Windows desktop application for static file inspection, folder snapshots and local change tracking. It helps examine file properties, calculate hashes, identify supported file formats from their headers, inspect embedded signing-certificate information, and compare folder states over time.

Built with **C#**, **WPF**, and **.NET 8**.

## Current features (v0.2.0)

| Feature | Description |
|---|---|
| File selection | Select a file through the picker or drag and drop it into the scanner. |
| SHA-256 | Calculate a SHA-256 hash using streamed file reads. |
| File identification | Detect supported formats from file headers, including PE, PDF, PNG, JPEG, GIF, ZIP, 7-Zip, RAR and ELF. |
| Extension comparison | Compare a file extension with its detected header format where supported. |
| Signature information | Inspect whether embedded signing-certificate information can be read. This does not validate the certificate trust chain. |
| Findings | Display heuristic indicators with explanations and highlight suspicious document-like executable names. |
| JSON export | Export the file inspection result as JSON. |
| Snapshot Engine — M1 | Create a read-only point-in-time inventory of a selected folder or drive with relative paths, sizes, timestamps, attributes and SHA-256 hashes. |
| Snapshot history | Save snapshots as immutable JSON files, list local history and load a saved snapshot without overwriting older snapshots. |
| Async scanning | Show progress, processed-file count and skipped-file count, with cancellation and access-error handling. |
| Change Detection — M2 | Compare two snapshots by relative path and SHA-256 and classify `ADDED`, `MODIFIED`, `DELETED`, `MOVED` and `RENAMED` files. |
| Duplicate-hash matching | Match duplicate hashes once each in stable relative-path order and record ambiguous matches in the report. |
| Partial scan protection | Preserve snapshot issues and suppress trusted deletions under paths that were unavailable during the new scan. |
| AUTO CHECK — M3 | Find the latest snapshot for the selected `RootPath`, create a baseline on the first run, then scan, compare, save a new snapshot and save a report. |
| Change Tracking UI — M4 | Browse check history, filter changes, view counters and inspect details for a selected change. |
| JSON reports | Store reports separately in `%LOCALAPPDATA%\\ANUXES\\AFTERTRACE\\Reports` and export the selected report as JSON. |

## Roadmap

- [x] M1 — Snapshot Engine
- [x] M2 — Change Detection
- [x] M3 — AUTO CHECK
- [x] M4 — UI & Reports
- [ ] Additional file signature formats
- [ ] Authenticode trust-chain verification
- [ ] Microsoft Defender integration

## Requirements

- Windows 10 or Windows 11.
- .NET 8 SDK.
- Visual Studio 2022 with the **.NET desktop development** workload (optional, recommended).

## Build and run

Clone the repository:

```powershell
git clone https://github.com/Kas1mov/ANUXES-AFTERTRACE.git
cd ANUXES-AFTERTRACE
```

Restore and build:

```powershell
dotnet restore .\ANUXES-AFTERTRACE.sln
dotnet build .\ANUXES-AFTERTRACE.sln -c Debug
dotnet build .\ANUXES-AFTERTRACE.sln -c Release
```

Run the application:

```powershell
dotnet run --project .\Aftertrace.App.csproj
```

To publish a self-contained Windows x64 build, run:

```powershell
.\build-release.bat
```

The publish output is written to `publish\win-x64\`.

## Project structure

```text
ANUXES-AFTERTRACE/
├── Assets/
│   ├── anuxes-logo.ico
│   └── anuxes-logo.png
├── Models/
│   ├── FileAnalysisResult.cs
│   ├── Finding.cs
│   ├── FileChange.cs
│   ├── FileChangeType.cs
│   ├── ChangeSummary.cs
│   ├── ChangeReport.cs
│   ├── ChangeReportSummary.cs
│   ├── Snapshot.cs
│   ├── SnapshotFileEntry.cs
│   ├── SnapshotIssue.cs
│   ├── SnapshotProgress.cs
│   ├── SnapshotSummary.cs
│   └── SnapshotComparisonResult.cs
├── Services/
│   ├── FileAnalyzer.cs
│   ├── SnapshotService.cs
│   ├── SnapshotComparisonService.cs
│   └── ChangeReportService.cs
├── docs/
│   └── README_RU.md
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── ANUXES-AFTERTRACE.sln
├── Aftertrace.App.csproj
├── build-release.bat
└── README.md
```

## Notes and limitations

- AFTERTRACE is a file-inspection assistant, **not a full antivirus**.
- A warning is not proof of malware; unsigned files and unknown formats can be legitimate.
- A result with no warnings does not prove that a file is safe.
- The embedded-certificate check reports available certificate information; it does not establish certificate-chain trust.
- Files are inspected as data. AFTERTRACE does not execute, edit, move, delete or quarantine inspected files.
- Snapshots are inventories, not backups: source files are never copied or changed.
- Symbolic links and junctions are skipped rather than followed.
- If a scan is partial, the comparison report stays partial and avoids treating unavailable paths as confirmed deletions.
- Duplicate SHA-256 matches use stable relative-path ordering and are marked as ambiguous in reports when the pairing is not unique.

## Credits

| Role | Name |
|---|---|
| Publisher | **ANUXES DIVISION** |
| Developer | **Kas1mov** |

## License

No open-source license has been granted for this repository. Unless a license is added, all rights are reserved by the developer. Public visibility does not grant permission to redistribute, modify, or reuse the source code.

---

<div align="center">

**ANUXES AFTERTRACE** · File Inspection · Change Tracking · `v0.2.0`

*Published by ANUXES DIVISION · Developed by Kas1mov*

</div>
