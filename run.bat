@echo off
cd /d "%~dp0"

REM Check for cmake
where cmake >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: cmake is not in PATH.
    echo.
    echo Options:
    echo   1. Open "x64 Native Tools Command Prompt" and run this script from there
    echo   2. Add cmake to your PATH (e.g. C:\Program Files\CMake\bin)
    echo   3. Build via Visual Studio or Qt Creator instead
    echo.
    pause
    exit /b 1
)

echo ========================================
echo  Building Makou Reactor (Release)
echo ========================================
echo.

REM Skip configure if already done
if not exist ".build\Release\CMakeCache.txt" (
    echo [1/2] Configuring...
    cmake --preset Release
    if %errorlevel% neq 0 (
        echo.
        echo Configure failed.
        pause
        exit /b %errorlevel%
    )
) else (
    echo [1/2] Already configured, skipping.
)

echo.
echo [2/2] Building...
cmake --build --preset Release
if %errorlevel% neq 0 (
    echo.
    echo Build failed.
    pause
    exit /b %errorlevel%
)

echo.
echo ========================================
echo  Launching Makou Reactor
echo ========================================
echo.

start "" .build\Release\Makou_Reactor.exe
