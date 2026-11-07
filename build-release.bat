@echo off
setlocal
cd /d "%~dp0"

echo ==========================================
echo   ANUXES AFTERTRACE - Release Publisher
echo ==========================================
echo.

if not exist ".\Aftertrace.App.csproj" (
    echo ERROR: Aftertrace.App.csproj was not found in:
    echo %cd%
    pause
    exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK was not found. Install the .NET 8 SDK first.
    pause
    exit /b 1
)

dotnet publish ".\Aftertrace.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ".\publish\win-x64"
if errorlevel 1 (
    echo.
    echo Publish failed. Check the .NET SDK installation and build output.
    pause
    exit /b 1
)

echo.
echo Build completed: %cd%\publish\win-x64
echo.
pause
