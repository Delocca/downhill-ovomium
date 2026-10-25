@echo off
rem Desinstalleur LmdMulti : lance fichiers\scripts\Uninstall.ps1 (Windows PowerShell).
rem Fichier en ASCII seulement (la console cmd lit mal les accents).
setlocal
title LmdMulti - desinstallation
set "SCRIPT=%~dp0fichiers\scripts\Uninstall.ps1"
if not exist "%SCRIPT%" goto notextracted
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
rem Codes 1 a 19 : erreur imprevue, on garde la console ouverte pour la lire (20 et plus : annulation deja expliquee).
set "CODE=%errorlevel%"
if %CODE% GEQ 1 if %CODE% LSS 20 pause
exit /b %CODE%

:notextracted
echo.
echo  Le zip n'est pas extrait.
echo  Faites un clic droit sur le fichier .zip, "Extraire tout...",
echo  puis lancez "Desinstaller LmdMulti" depuis le dossier extrait.
echo.
pause
exit /b 1
