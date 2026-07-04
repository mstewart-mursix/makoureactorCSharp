@echo off
setlocal

cd /d "%~dp0"

echo ========================================
echo  MakouReactor.CSharp - Publish
echo ========================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: dotnet is not in PATH.
    echo Install the .NET 8 SDK or run this from a developer prompt.
    echo.
    pause
    exit /b 1
)

set "PUBLISH_ROOT=%~dp0..\.dist\publish"
set "GUI_OUT=%PUBLISH_ROOT%\makoureactor-gui-win64"
set "CLI_OUT=%PUBLISH_ROOT%\makoureactor-cli-win64"
set "CONFIG_SRC=%~dp0config"

echo [1/3] Cleaning publish folders...
if exist "%GUI_OUT%" rmdir /s /q "%GUI_OUT%"
if exist "%CLI_OUT%" rmdir /s /q "%CLI_OUT%"
mkdir "%GUI_OUT%"
mkdir "%CLI_OUT%"

echo.
echo [2/3] Publishing GUI package...
dotnet publish src\MakouReactor.UI.WPF\MakouReactor.UI.WPF.csproj -c Release -r win-x64 --self-contained false -o "%GUI_OUT%"
if errorlevel 1 (
    echo.
    echo GUI publish failed.
    pause
    exit /b 1
)

echo Copying GUI config templates...
xcopy /y "%CONFIG_SRC%\*.template.json" "%GUI_OUT%\" >nul
if errorlevel 1 (
    echo.
    echo ERROR: Failed to copy GUI config templates.
    pause
    exit /b 1
)

echo.
echo [3/3] Publishing CLI package...
dotnet publish src\MakouReactor.CLI\MakouReactor.CLI.csproj -c Release -r win-x64 --self-contained false -o "%CLI_OUT%"
if errorlevel 1 (
    echo.
    echo CLI publish failed.
    pause
    exit /b 1
)

echo Copying CLI config templates...
xcopy /y "%CONFIG_SRC%\*.template.json" "%CLI_OUT%\" >nul
if errorlevel 1 (
    echo.
    echo ERROR: Failed to copy CLI config templates.
    pause
    exit /b 1
)

if not exist "%GUI_OUT%\MakouReactor.UI.WPF.exe" (
    echo.
    echo ERROR: GUI package is missing MakouReactor.UI.WPF.exe.
    pause
    exit /b 1
)

if not exist "%GUI_OUT%\MakouReactor.UI.WPF.dll" (
    echo.
    echo ERROR: GUI package is missing MakouReactor.UI.WPF.dll.
    pause
    exit /b 1
)

if not exist "%GUI_OUT%\MakouReactor.UI.WPF.runtimeconfig.json" (
    echo.
    echo ERROR: GUI package is missing MakouReactor.UI.WPF.runtimeconfig.json.
    pause
    exit /b 1
)

if not exist "%GUI_OUT%\MakouReactor.UI.WPF.deps.json" (
    echo.
    echo ERROR: GUI package is missing MakouReactor.UI.WPF.deps.json.
    pause
    exit /b 1
)

if not exist "%GUI_OUT%\settings.template.json" (
    echo.
    echo ERROR: GUI package is missing settings.template.json.
    pause
    exit /b 1
)

if not exist "%GUI_OUT%\llm.config.template.json" (
    echo.
    echo ERROR: GUI package is missing llm.config.template.json.
    pause
    exit /b 1
)

if not exist "%CLI_OUT%\MakouReactor.CLI.exe" (
    echo.
    echo ERROR: CLI package is missing MakouReactor.CLI.exe.
    pause
    exit /b 1
)

if not exist "%CLI_OUT%\MakouReactor.CLI.dll" (
    echo.
    echo ERROR: CLI package is missing MakouReactor.CLI.dll.
    pause
    exit /b 1
)

if not exist "%CLI_OUT%\MakouReactor.CLI.runtimeconfig.json" (
    echo.
    echo ERROR: CLI package is missing MakouReactor.CLI.runtimeconfig.json.
    pause
    exit /b 1
)

if not exist "%CLI_OUT%\MakouReactor.CLI.deps.json" (
    echo.
    echo ERROR: CLI package is missing MakouReactor.CLI.deps.json.
    pause
    exit /b 1
)

if not exist "%CLI_OUT%\llm.config.template.json" (
    echo.
    echo ERROR: CLI package is missing llm.config.template.json.
    pause
    exit /b 1
)

for /r "%GUI_OUT%" %%F in (*.lgp *.DAT *.lzs *.dec *.iso *.bin *.img) do (
    echo.
    echo ERROR: GUI package contains possible game data: %%F
    pause
    exit /b 1
)

for /r "%CLI_OUT%" %%F in (*.lgp *.DAT *.lzs *.dec *.iso *.bin *.img) do (
    echo.
    echo ERROR: CLI package contains possible game data: %%F
    pause
    exit /b 1
)

echo.
echo Published:
echo   GUI: %GUI_OUT%
echo   CLI: %CLI_OUT%
echo.

endlocal
