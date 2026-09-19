@echo off
setlocal

rem  Rebuilds the site's data ready for pushing to main.
rem
rem  Everything the Pages workflow does, it does on a runner with no NMSE checkout - so the
rem  technology extraction cannot happen there, and its output is committed instead. That
rem  makes this the one step a push cannot do for itself: re-read NMSE's resources, then
rem  build and test against what came out, so what is committed is what the site publishes.
rem
rem  Run it after the game updates and after NMSE refreshes its mapping or item data.
rem
rem  Pass the path to NMSE's Resources folder, or let it guess the sibling checkout:
rem      build-site.cmd [path-to-NMSE-Resources]

cd /d "%~dp0"

set "NMSE=%~1"
if "%NMSE%"=="" set "NMSE=..\NMSE\Resources"

if not exist "%NMSE%\json" (
    echo Could not find NMSE resources at "%NMSE%".
    echo Pass the path as the first argument.
    exit /b 1
)

echo == building
dotnet build -c Release || exit /b 1

echo.
echo == extracting technology from "%NMSE%"
dotnet run --project tools\NmsVault.Ingest -c Release -- extract-tech --nmse "%NMSE%" || exit /b 1

echo.
echo == validating the gallery
dotnet run --project tools\NmsVault.Ingest -c Release -- validate || exit /b 1

echo.
echo == tests
dotnet test tests\NmsVault.Json.Tests -c Release --nologo || exit /b 1
dotnet test tests\NmsVault.Core.Tests -c Release --nologo || exit /b 1

echo.
echo Done. Commit tech.json and img\tech if the extraction changed them.
