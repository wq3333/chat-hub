@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem ============================================================
rem  ChatHub updater
rem
rem  Usage:
rem    upgrade.bat check      Print the latest GitHub release as one JSON line.
rem    upgrade.bat upgrade    Apply the downloaded win-x64.zip package.
rem
rem  Optional environment variables:
rem    CHATHUB_UPGRADE_ZIP    Package to install.
rem                           Default: %LOCALAPPDATA%\ChatHub\update\win-x64.zip
rem    CHATHUB_INSTALL_DIR    Directory that contains ChatHub.exe.
rem                           Default: the directory of this script.
rem    CHATHUB_UPGRADE_LOG    Log file.
rem                           Default: <package folder>\upgrade.log
rem
rem  The repository slug below is intentionally kept in sync with
rem  ChatHub.Services.AppInfo.RepositorySlug.
rem ============================================================

set "MODE=%~1"

if /I "%MODE%"=="check" goto :check
if /I "%MODE%"=="upgrade" goto :start_detached
if /I "%MODE%"=="--detached" goto :upgrade
goto :usage

:usage
echo Usage:
echo   upgrade.bat check
echo   upgrade.bat upgrade
echo.
echo Optional environment variables:
echo   CHATHUB_UPGRADE_ZIP, CHATHUB_INSTALL_DIR, CHATHUB_UPGRADE_LOG
exit /b 2

rem ------------------------------------------------------------
rem  check: query the latest GitHub release and print JSON
rem ------------------------------------------------------------
:check
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; try { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; $headers=@{ 'User-Agent'='ChatHub'; 'Accept'='application/vnd.github+json' }; if ($env:GITHUB_TOKEN) { $headers['Authorization']='Bearer ' + $env:GITHUB_TOKEN }; $repo='wq3333/chat-hub'; $r=Invoke-RestMethod -Uri ('https://api.github.com/repos/' + $repo + '/releases/latest') -Headers $headers; $a=@($r.assets) | Where-Object { $_.name -eq 'win-x64.zip' } | Select-Object -First 1; if (-not $a) { throw 'The latest release has no win-x64.zip asset.' }; [pscustomobject]@{ version=($r.tag_name -replace '^[vV]',''); tag_name=$r.tag_name; name=$r.name; body=$r.body; html_url=$r.html_url; download_url=$a.browser_download_url; published_at=$r.published_at; size=$a.size } | ConvertTo-Json -Compress } catch { [pscustomobject]@{ error=$_.Exception.Message } | ConvertTo-Json -Compress; exit 1 }"
exit /b %ERRORLEVEL%

rem ------------------------------------------------------------
rem  upgrade: copy this script to TEMP and relaunch it detached.
rem  The install directory (including this file) is replaced by
rem  the detached copy, so the running script is never overwritten.
rem
rem  Use "start /b cmd /c" instead of "start" directly: starting a .bat
rem  creates a "cmd /K" console that stays open after the update, which
rem  looks like a black cmd window. /b keeps the (hidden) parent console
rem  and /c makes the updater process exit when it is done.
rem ------------------------------------------------------------
:start_detached
if not defined CHATHUB_INSTALL_DIR set "CHATHUB_INSTALL_DIR=%~dp0"
call :trim_install_dir
if not defined CHATHUB_UPGRADE_ZIP if defined LOCALAPPDATA set "CHATHUB_UPGRADE_ZIP=%LOCALAPPDATA%\ChatHub\update\win-x64.zip"
if not defined CHATHUB_UPGRADE_ZIP set "CHATHUB_UPGRADE_ZIP=%TEMP%\ChatHub\update\win-x64.zip"
if not defined CHATHUB_UPGRADE_LOG for %%I in ("%CHATHUB_UPGRADE_ZIP%") do set "CHATHUB_UPGRADE_LOG=%%~dpIupgrade.log"

if not exist "%TEMP%\ChatHub\" mkdir "%TEMP%\ChatHub" >nul 2>&1
set "CHATHUB_TEMP_BAT=%TEMP%\ChatHub\upgrade.bat"
copy /y "%~f0" "%CHATHUB_TEMP_BAT%" >nul
if errorlevel 1 (
    echo [ERROR] Cannot create the temporary updater copy in %TEMP%\ChatHub.
    exit /b 1
)

start "" /b "%COMSPEC%" /c ""%CHATHUB_TEMP_BAT%" --detached"
exit /b 0

rem ------------------------------------------------------------
rem  detached: stop the app, extract the package, restart it,
rem  then delete the package on success.
rem ------------------------------------------------------------
:upgrade
if not defined CHATHUB_INSTALL_DIR set "CHATHUB_INSTALL_DIR=%~dp0"
call :trim_install_dir
if not defined CHATHUB_UPGRADE_ZIP if defined LOCALAPPDATA set "CHATHUB_UPGRADE_ZIP=%LOCALAPPDATA%\ChatHub\update\win-x64.zip"
if not defined CHATHUB_UPGRADE_ZIP set "CHATHUB_UPGRADE_ZIP=%TEMP%\ChatHub\update\win-x64.zip"
if not defined CHATHUB_UPGRADE_LOG for %%I in ("%CHATHUB_UPGRADE_ZIP%") do set "CHATHUB_UPGRADE_LOG=%%~dpIupgrade.log"

for %%I in ("%CHATHUB_UPGRADE_LOG%") do if not exist "%%~dpI" mkdir "%%~dpI" >nul 2>&1
call :log "Updater started."
call :log "Package: %CHATHUB_UPGRADE_ZIP%"
call :log "Install: %CHATHUB_INSTALL_DIR%"

if not exist "%CHATHUB_UPGRADE_ZIP%" (
    call :fail "Update package not found: %CHATHUB_UPGRADE_ZIP%"
    exit /b 1
)
if not exist "%CHATHUB_INSTALL_DIR%\ChatHub.exe" (
    call :fail "ChatHub.exe was not found in the install directory."
    exit /b 1
)

rem Give the app a moment to shut down by itself. If it is still
rem running after 5 seconds, terminate it.
set /a WAITED=0
:wait_loop
tasklist /FI "IMAGENAME eq ChatHub.exe" /FO CSV /NH 2>nul | find /I "ChatHub.exe" >nul
if errorlevel 1 goto :app_closed
set /a WAITED+=1
if %WAITED% GEQ 5 goto :kill_app
ping -n 2 127.0.0.1 >nul 2>&1
goto :wait_loop

:kill_app
call :log "ChatHub.exe is still running; terminating it."
taskkill /F /T /IM ChatHub.exe >nul 2>&1
ping -n 2 127.0.0.1 >nul 2>&1

:app_closed
set "CHATHUB_STAGE=%TEMP%\ChatHub\stage-%RANDOM%-%RANDOM%"
mkdir "%CHATHUB_STAGE%" >nul 2>&1
call :log "Extracting the update package."
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; Expand-Archive -LiteralPath $env:CHATHUB_UPGRADE_ZIP -DestinationPath $env:CHATHUB_STAGE -Force"
if errorlevel 1 (
    call :fail "Failed to extract the update package."
    exit /b 1
)

if not exist "%CHATHUB_STAGE%\ChatHub.exe" (
    call :fail "The update package does not contain ChatHub.exe."
    exit /b 1
)

call :log "Copying files into the install directory."
robocopy "%CHATHUB_STAGE%" "%CHATHUB_INSTALL_DIR%" /E /R:2 /W:2 /NFL /NDL /NJH /NJS >>"%CHATHUB_UPGRADE_LOG%" 2>&1
if errorlevel 8 (
    call :fail "Failed to copy update files (robocopy exit code %ERRORLEVEL%)."
    exit /b 1
)

call :log "Restarting ChatHub."
start "" "%CHATHUB_INSTALL_DIR%\ChatHub.exe"

del /f /q "%CHATHUB_UPGRADE_ZIP%" >nul 2>&1
if errorlevel 1 (
    call :log "Warning: the update package could not be deleted."
) else (
    call :log "Update package deleted."
)

if exist "%CHATHUB_STAGE%" rd /s /q "%CHATHUB_STAGE%" >nul 2>&1
call :log "Update finished successfully."
exit /b 0

rem ------------------------------------------------------------
rem  helpers
rem ------------------------------------------------------------
:log
>>"%CHATHUB_UPGRADE_LOG%" echo [%DATE% %TIME%] "%~1"
exit /b 0

:fail
set "CHATHUB_FAIL_MSG=%~1"
call :log "ERROR: %CHATHUB_FAIL_MSG%"
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "Add-Type -AssemblyName PresentationFramework; [System.Windows.MessageBox]::Show([string]$env:CHATHUB_FAIL_MSG, 'ChatHub update failed') | Out-Null"
if exist "%CHATHUB_STAGE%" rd /s /q "%CHATHUB_STAGE%" >nul 2>&1
call :restart_if_needed
exit /b 1

:restart_if_needed
tasklist /FI "IMAGENAME eq ChatHub.exe" /FO CSV /NH 2>nul | find /I "ChatHub.exe" >nul
if not errorlevel 1 exit /b 0
if exist "%CHATHUB_INSTALL_DIR%\ChatHub.exe" start "" "%CHATHUB_INSTALL_DIR%\ChatHub.exe"
exit /b 0

rem Trailing backslashes break quoted command-line arguments (robocopy sees
rem "...\dir\" as an escaped quote). Keep the root "C:\" untouched.
:trim_install_dir
if not "%CHATHUB_INSTALL_DIR:~1%"==":\" if "%CHATHUB_INSTALL_DIR:~-1%"=="\" set "CHATHUB_INSTALL_DIR=%CHATHUB_INSTALL_DIR:~0,-1%"
exit /b 0
