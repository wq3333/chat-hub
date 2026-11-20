@echo off
setlocal
rem ============================================================
rem  Build ChatHub with the settings in
rem  src\Properties\PublishProfiles\FolderProfile.pubxml
rem  (Release / win-x64 / self-contained / single-file).
rem ============================================================

pushd "%~dp0"
if errorlevel 1 (
    echo [ERROR] Cannot enter the repository root.
    exit /b 1
)

echo Publishing ChatHub (FolderProfile, win-x64)...
echo.
dotnet publish "src\ChatHub.csproj" -p:PublishProfile=FolderProfile
set "EXITCODE=%ERRORLEVEL%"
echo.

set "PUBLISH_DIR=src\bin\Release\net10.0-windows\publish\win-x64"

if not "%EXITCODE%"=="0" goto :finish

if not exist "%PUBLISH_DIR%\ChatHub.exe" (
    echo [ERROR] Publish finished but ChatHub.exe was not found in:
    echo         %CD%\%PUBLISH_DIR%
    set "EXITCODE=1"
    goto :finish
)

if not exist "%PUBLISH_DIR%\upgrade.bat" (
    echo [ERROR] Publish finished but upgrade.bat was not found in:
    echo         %CD%\%PUBLISH_DIR%
    set "EXITCODE=1"
    goto :finish
)

echo Publish output: %CD%\%PUBLISH_DIR%

:finish
popd
exit /b %EXITCODE%
