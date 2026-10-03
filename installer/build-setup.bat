@echo off
REM build-setup.bat — run on Windows from the repo root.
REM 1) Publishes FileOrganizer.exe (self-contained, single file)
REM 2) Builds "File Organizer Setup.exe" with Inno Setup (ISCC must be on PATH or in the default install dir).
setlocal
cd /d %~dp0\..

dotnet publish src\FileOrganizer.App\FileOrganizer.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o installer\publish
if errorlevel 1 exit /b 1

set ISCC=
where ISCC >nul 2>nul && set ISCC=ISCC
if "%ISCC%"=="" if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set ISCC="%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if "%ISCC%"=="" if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set ISCC="%ProgramFiles%\Inno Setup 6\ISCC.exe"
if "%ISCC%"=="" (
  echo Inno Setup 6 not found. The published exe is ready in installer\publish\
  echo Install Inno Setup from https://jrsoftware.org/isdl.php then re-run this script to get the Setup exe.
  exit /b 0
)

%ISCC% installer\FileOrganizer.iss
if errorlevel 1 exit /b 1
echo Done: installer\output\File Organizer Setup.exe
