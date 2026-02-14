@echo off
:: This script sets up the Port Listener to run automatically on Windows startup.
:: It creates a hidden VBScript and places it in your Startup folder.

set "PROJECT_DIR=%~dp0"
set "EXE_PATH=%PROJECT_DIR%publish\PortListener.exe"
set "VBS_SCRIPT=%PROJECT_DIR%StartPortListener.vbs"
set "STARTUP_FOLDER=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"

echo [1/3] Publishing the application in Release mode...
dotnet publish --configuration Release -o "%PROJECT_DIR%publish"

echo [2/3] Creating hidden launcher script...
(
echo Set WshShell = CreateObject^("WScript.Shell"^)
echo WshShell.CurrentDirectory = "%PROJECT_DIR%"
echo WshShell.Run """%EXE_PATH%""", 0, False
) > "%VBS_SCRIPT%"

echo [3/3] Adding to Startup folder...
copy /y "%VBS_SCRIPT%" "%STARTUP_FOLDER%\StartPortListener.vbs"

echo.
echo ======================================================
echo Setup Complete! 
echo The Port Listener will now start HIDDEN in the background 
echo whenever the computer starts and you log in.
echo ======================================================
echo.
pause
