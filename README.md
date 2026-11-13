# ANUXES AFTERTRACE

**Configured project version:** 0.2.0
**Developer:** Kas1mov
**Publisher / brand:** ANUXES DIVISION
**Platform:** Windows 10/11, .NET 8, WPF

ANUXES AFTERTRACE is a lightweight local file-inspection assistant. It gathers file metadata, calculates hashes, and records folder snapshots to help investigate changes over time. It is **not an antivirus** and does not claim to determine whether a file is safe or malicious.

## Current features

### File scanner

- Select a file through the picker or drag and drop it into the scanner.
- Calculate a SHA-256 hash using a streamed read.
- Detect supported file formats from their headers (including PE, PDF, PNG, JPEG, GIF, ZIP, 7-Zip, RAR and ELF).
- Compare the selected extension with the detected format where supported.
- Highlight executable files with document-like double extensions.
- Read available information about an embedded signing certificate. This does **not** validate the certificate trust chain.
- Display findings with explanations and export the result as JSON.

### Snapshot Engine — M1

- Select a folder or drive and create a point-in-time snapshot.
- Record each readable file's relative path, size, last-write timestamp, attributes and SHA-256 hash.
- Save snapshots as JSON in `%LOCALAPPDATA%\ANUXES\AFTERTRACE\Snapshots`.
- View the local snapshot history and load a saved snapshot back into the application.
- Show scan progress, captured-file count, total size and skipped-item count; cancel an in-progress scan.
- Skip symbolic links and junctions rather than following paths outside the selected tree.
- Record inaccessible or changing items as snapshot issues instead of silently treating them as captured.

Snapshots are inventories, not backups: AFTERTRACE does not copy the source files. Source files are read only; they are never executed, edited, moved, deleted or quarantined.

### Change Detection, AUTO CHECK and reports — M2–M4

- Compare two snapshots by relative path and SHA-256.
- Classify `ADDED`, `MODIFIED`, `DELETED`, `MOVED` and `RENAMED` files. A folder and filename change is shown as `MOVED`.
- Match duplicate hashes once each in stable relative-path order and mark ambiguous matches in the report.
- Treat snapshots with issues as partial and suppress deletions under paths that were unavailable during the new scan.
- Run `AUTO CHECK` against the latest snapshot for the selected `RootPath`; the first run creates a baseline, and later runs save a new snapshot plus a comparison report.
- Show scan progress, processed and skipped counts, cancellation, snapshot history, check history, change filters, counters and selected-change details.
- Save reports separately in `%LOCALAPPDATA%\\ANUXES\\AFTERTRACE\\Reports` and export the selected report as JSON.

## Important limitations

- A warning is not proof of malware. Unsigned files, unknown formats and extension mismatches can be legitimate.
- A result with no warnings does not mean a file is safe.
- There is no malware-signature database, cloud reputation lookup, behavioral sandbox or real-time protection.
- The certificate check only reports whether certificate information can be read. It does not establish publisher identity or signature trust.
- Symbolic links and junctions are skipped. A file that changes while its hash is being calculated is reported as an issue and is not included as a stable entry.

## Build

Requirements:

- Windows 10 or Windows 11
- .NET 8 SDK

The project file is in the repository root. From this directory, run:

```powershell
dotnet restore .\ANUXES-AFTERTRACE.sln
dotnet build .\ANUXES-AFTERTRACE.sln -c Release
dotnet run --project .\Aftertrace.App.csproj
```

To publish a self-contained Windows x64 build, run `build-release.bat`.

## Project structure

```text
ANUXES-AFTERTRACE/
├── Assets/                 # ANUXES application icons
├── Models/                 # File analysis and snapshot models
├── Services/               # File analyzer and snapshot engine
├── docs/
│   └── README_RU.md         # Russian documentation
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── Aftertrace.App.csproj
├── ANUXES-AFTERTRACE.sln
├── build-release.bat
└── README.md
```

## Roadmap

- [x] M1 — Folder and drive snapshots
- [x] M2 — Before/after comparison and change detection
- [x] M3 — Automatic check against the latest snapshot
- [x] M4 — Change history, filters and reports
- [ ] More file signature formats
- [ ] Authenticode trust verification
- [ ] Microsoft Defender integration

---

© 2026 ANUXES DIVISION. All rights reserved.
