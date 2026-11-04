# ANUXES AFTERTRACE

**Version:** 0.1.2 (visual polish preview)  
**Developer:** Kas1mov  
**Publisher / brand:** ANUXES DIVISION  
**Platform:** Windows 10/11, .NET 8, WPF

ANUXES AFTERTRACE is a lightweight file-inspection assistant. It gathers useful file facts and highlights a few patterns that may deserve a closer look. It is **not an antivirus** and does not claim to determine whether a file is safe or malicious.

## Included in v0.1.0

### v0.1.2 visual polish

- Reworked the canvas from blue-navy to a deeper charcoal/purple-black tone to match the ANUXES LOGLENS family.
- Made the AFTERTRACE title in the top bar purple with a soft neon glow.
- Added a custom purple scrollbar thumb and a dark track; removed the default bright scrollbar appearance.
- Added a dark, purple-focused TextBox template and custom title-bar button visuals.
- Added shared dark styling for ordinary buttons so future controls stay visually consistent.

- Select a file through the picker or drag and drop it into the scanner.
- Calculate a SHA-256 hash using a streamed read.
- Detect several common file formats from their file headers (PE, PDF, PNG, JPEG, GIF, ZIP, 7-Zip, RAR and ELF).
- Compare selected extensions with detected file types for supported formats.
- Highlight executable files with document-like double extensions.
- Check whether an embedded signing certificate can be read from a file. The app does **not** validate the certificate trust chain in this version.
- Display findings with explanations and export the result as JSON.
- Files are inspected as data; AFTERTRACE does not execute, delete, or quarantine them.

## Important limitations

- A warning is not proof of malware. Unsigned files, unknown formats and extension mismatches can be legitimate.
- A result with no warnings does not mean a file is safe.
- There is no malware-signature database, cloud reputation lookup, behavioral sandbox, or real-time protection in v0.1.0.
- The embedded-certificate check only reports whether a certificate can be extracted. It does not establish publisher identity or signature trust.
- Folder snapshots, before/after installation comparison, change timelines and Microsoft Defender integration are planned for later iterations, not implemented in this preview.

## Build

Requirements:

- Windows 10 or Windows 11
- .NET 8 SDK

Open `Aftertrace.App/Aftertrace.App.csproj` in Visual Studio 2022, or run from this directory:

```powershell
dotnet restore .\Aftertrace.App\Aftertrace.App.csproj
dotnet build .\Aftertrace.App\Aftertrace.App.csproj -c Release
dotnet run --project .\Aftertrace.App\Aftertrace.App.csproj
```

To publish a self-contained Windows x64 build, run `build-release.bat`.

## Project structure

```text
ANUXES-AFTERTRACE/
├── Aftertrace.App/
│   ├── Assets/             # ANUXES logo
│   ├── Models/             # Analysis result models
│   ├── Services/           # Static file inspection logic
│   ├── App.xaml
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
├── build-release.bat
└── README.md
```

## Roadmap

- [ ] File and folder snapshots
- [ ] Before/after comparison and change timeline
- [ ] Microsoft Defender scan integration
- [ ] More file signature formats
- [ ] Authenticode trust verification
- [ ] Searchable analysis history
- [ ] Improved report export

---

© 2026 ANUXES DIVISION. All rights reserved.


## UI polish in v0.1.2

- Transparent WPF window surface with a rounded custom frame and a soft violet outer glow.
- Removed the default resize-grip presentation; the window remains resizable.
- Animated hover feedback for primary, secondary, sidebar, and window-control buttons.
- Subtle elastic grab/release response when dragging the title bar.
- Rounded content clipping follows the window size and switches to square corners when maximized.

The elastic effect is intentionally restrained: it adds a small squish on grab and a spring-like settle on release without making the window difficult to control.
