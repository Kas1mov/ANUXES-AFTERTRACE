<div align="center">

# ANUXES AFTERTRACE

### A lightweight file inspection assistant for Windows

**Inspect. Compare. Understand.**

![Version](https://img.shields.io/badge/version-0.1.1-9B75FF?style=for-the-badge)
![Platform](https://img.shields.io/badge/platform-Windows-263A63?style=for-the-badge&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)
![UI](https://img.shields.io/badge/UI-WPF-17151F?style=for-the-badge)

**Publisher:** ANUXES DIVISION · **Developer:** Kas1mov

<img width="1231" height="809" alt="ANUXES AFTERTRACE interface" src="https://github.com/user-attachments/assets/ee3ccbc7-9ae9-41a0-9f36-45f13c4ab1d2" />

</div>

---

## Overview

**ANUXES AFTERTRACE** is a Windows desktop application for static file inspection. It helps examine file properties, calculate hashes, identify supported file formats from their headers, inspect embedded signing-certificate information, and review heuristic findings.

Built with **C#**, **WPF**, and **.NET 8**.

## Current features (v0.1.1)

| Feature | Description |
|---|---|
| File selection | Select a file through the picker or drag and drop it into the scanner. |
| SHA-256 | Calculate a SHA-256 hash using streamed file reads. |
| File identification | Detect supported formats from file headers. |
| Extension comparison | Compare a file extension with its detected header format where supported. |
| Signature information | Inspect whether embedded signing-certificate information can be read. This version does not validate the certificate trust chain. |
| Findings | Display heuristic indicators with explanations. |
| JSON export | Export the inspection result as JSON. |

## Roadmap

The next development milestone is **Snapshot System / Change Tracking**:

- Save snapshots of a user-selected folder or drive.
- Compare a later scan with a saved snapshot.
- Identify added, modified, moved, renamed, and deleted files.
- Keep snapshot history and export comparison reports.

**Change Tracking is planned and is not available in v0.1.1.**

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
```

Run the application:

```powershell
dotnet run --project .\Aftertrace.App.csproj
```

To publish a self-contained Windows x64 build, run:

```powershell
.\build-release.bat
```

The output is written to `publish\win-x64\`.

## Project structure

```text
ANUXES-AFTERTRACE/
├── Assets/
│   ├── anuxes-logo.ico
│   └── anuxes-logo.png
├── Models/
│   ├── FileAnalysisResult.cs
│   └── Finding.cs
├── Services/
│   └── FileAnalyzer.cs
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
- The embedded-certificate check reports available certificate information; it does not establish certificate-chain trust in this version.
- Files are inspected as data. AFTERTRACE does not execute, delete, or quarantine inspected files.
- Snapshot-based Change Tracking is planned for a future release.

## Credits

| Role | Name |
|---|---|
| Publisher | **ANUXES DIVISION** |
| Developer | **Kas1mov** |

## License

No open-source license has been granted for this repository. Unless a license is added, all rights are reserved by the developer. Public visibility does not grant permission to redistribute, modify, or reuse the source code.

---

<div align="center">

**ANUXES AFTERTRACE** · File Inspection · `v0.1.1`

*Published by ANUXES DIVISION · Developed by Kas1mov*

</div>
