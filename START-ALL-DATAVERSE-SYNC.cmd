@echo off
setlocal
title Metasys Dataverse Sync Launchpad
echo Starting SQL and SharePoint Dataverse workers in separate windows...
echo Keep both worker windows open, then use Sync All now in FMC BMS Demo.
echo.
start "FMC SQL Sync Worker" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-DataverseSync.ps1" -Mode Run
start "FMC SharePoint Sync Worker" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-SpoDataverseWatch.ps1"
echo Two worker windows were opened.
echo Close this launchpad or press any key to exit; worker windows stay open.
pause >nul
