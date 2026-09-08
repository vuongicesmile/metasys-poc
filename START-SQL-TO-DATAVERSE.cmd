@echo off
setlocal
title Metasys SQL to Dataverse
echo Starting the SQL-to-Dataverse command worker...
echo Trigger the Power Automate flow to queue a sync request.
echo Keep this window open. Press Ctrl+C to stop.
echo Runbook: %~dp0docs\runbooks\sql-to-dataverse-runbook.vi.md
echo.
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0scripts\Start-DataverseSync.ps1" -Mode Run
set "syncExitCode=%ERRORLEVEL%"
if not "%syncExitCode%"=="0" (
    echo.
    echo The worker exited with code %syncExitCode%. Read the error above and the runbook.
    pause
)
exit /b %syncExitCode%
