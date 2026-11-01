@echo off
setlocal
cd /d "%~dp0"
echo Checking .NET SDK selection...
dotnet --version
if errorlevel 1 (
 echo Install a stable .NET 8 SDK first: https://dotnet.microsoft.com/download/dotnet/8.0
 pause
 exit /b 1
)
for /f "delims=." %%V in ('dotnet --version') do set MAJOR=%%V
if not "%MAJOR%"=="8" (
 echo ERROR: This project needs the stable .NET 8 SDK.
 echo Install .NET 8 SDK; global.json will then select it automatically.
 dotnet --list-sdks
 pause
 exit /b 1
)
if exist obj rmdir /s /q obj
if exist bin rmdir /s /q bin
dotnet restore MKW.WinUI.csproj -r win-x64
if errorlevel 1 (echo Restore failed. & pause & exit /b 1)
dotnet publish MKW.WinUI.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o publish
if errorlevel 1 (
 echo Build failed. If the error mentions Pri.Tasks or AppxPackage, install Visual Studio 2022 Windows application development tools.
 pause
 exit /b 1
)
echo Build complete. Run publish\MKW.WinUI.exe
pause
