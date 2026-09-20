@echo off
setlocal

rem  Everything a push to main does, run here first, plus the one thing it cannot do for itself.
rem
rem  The gallery is built from data\ by the web project, so that much a runner handles. Reading
rem  NMSE's resources it cannot: the technology icons, class badges and favicon come out of an
rem  NMSE checkout no runner has, which is why their output is committed. That extraction is the
rem  first step here, and whatever it changes should be committed with everything else.
rem
rem  The rest builds, validates, tests and publishes exactly as the runner does, so a failure is
rem  found here rather than as a failed deployment - or, worse, as a blank page.
rem
rem  Pass the path to NMSE's Resources folder, or let it guess the sibling checkout:
rem      build-site.cmd [path-to-NMSE-Resources]

cd /d "%~dp0"

set "NMSE=%~1"
if "%NMSE%"=="" set "NMSE=..\NMSE\Resources"

set "PAGES=%TEMP%\nmsvault-pages-check"

if not exist "%NMSE%\json" (
    echo Could not find NMSE resources at "%NMSE%".
    echo Pass the path as the first argument.
    exit /b 1
)

echo.
echo === Building, which also builds the gallery from data\ ===
dotnet build -c Release
if errorlevel 1 goto :failed

echo.
echo === Extracting technology from "%NMSE%" ===
dotnet run --project tools\NmsVault.Ingest -c Release --no-build -- extract-tech --nmse "%NMSE%"
if errorlevel 1 goto :failed

echo.
echo === Validating the gallery ===
dotnet run --project tools\NmsVault.Ingest -c Release --no-build -- validate
if errorlevel 1 goto :failed

echo.
echo === Testing ===
dotnet test -c Release --no-build
if errorlevel 1 goto :failed

echo.
echo === Publishing, as a rehearsal ===
if exist "%PAGES%" rmdir /s /q "%PAGES%"
dotnet publish src\NmsVault.Web -c Release -o "%PAGES%"
if errorlevel 1 goto :failed

rem  Pages runs the output through Jekyll without this, and Jekyll drops every folder whose name
rem  starts with an underscore - _framework among them, which is the whole application.
if not exist "%PAGES%\wwwroot\.nojekyll" (
    echo.
    echo FAILED: no .nojekyll in the published site. Pages would drop _framework.
    goto :failed
)

rem  A site published with base href="/" resolves every asset against the domain root and comes
rem  up blank, with nothing in the console to say why.
findstr /c:"<base href=\"/NMS-Vault/\" />" "%PAGES%\wwwroot\index.html" >nul
if errorlevel 1 (
    echo.
    echo FAILED: index.html still has base href="/". Every asset would resolve against the domain root.
    goto :failed
)

rem  A generated gallery that generated nothing is a build that quietly did nothing.
if not exist "%PAGES%\wwwroot\gallery\index.json" (
    echo.
    echo FAILED: no gallery index in the published site.
    goto :failed
)

rmdir /s /q "%PAGES%"

echo.
echo === Ready ===
git status --short
echo.
echo Commit whatever is listed above - tech.json and img\tech if the extraction changed them -
echo and push to main. Pages publishes on the push.
exit /b 0

:failed
echo.
echo === Not ready. Fix the failure above before pushing. ===
exit /b 1
