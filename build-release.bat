@echo off
setlocal
cd /d "%~dp0"
echo ==========================================
echo   ANUXES AFTERTRACE - Release Publisher
echo ==========================================
dotnet publish ".\Aftertrace.App.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ".\publish\win-x64"
if errorlevel 1 (
    echo.
    echo Publish failed. Make sure the .NET 8 SDK is installed.
    pause
    exit /b 1
)
echo.
echo Build completed: %cd%\publish\win-x64
echo.
pause
