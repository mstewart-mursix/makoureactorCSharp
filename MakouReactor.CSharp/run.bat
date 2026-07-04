@echo off
cd /d "%~dp0"

echo ========================================
echo  MakouReactor.CSharp - Build + Test
echo ========================================
echo.

dotnet test tests\MakouReactor.Tests\MakouReactor.Tests.csproj -c Release

echo.
echo Done. Press any key to exit.
pause >nul