@ECHO OFF
SETLOCAL
REM Compatibility entry point. One PowerShell orchestrator holds the release lease while it
REM cleans exact output leaves, rebuilds every classic tuple, receipts all inputs, and publishes.
REM The collection plan ships eng\release\Install-CUEToolsPlugin.ps1 as Install-CUEToolsPlugin.ps1.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0eng\release\Invoke-ClassicRelease.ps1"
SET COLLECT_EXIT=%ERRORLEVEL%
ENDLOCAL & EXIT /B %COLLECT_EXIT%
