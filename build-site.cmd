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

rem  The script index.html asks for has to be the one that was published. Publish renames it to
rem  carry a content hash and the page names it through a placeholder the SDK fills in, so the
rem  two can disagree - and when they do, every page loads its shell and never starts.
rem
rem  Done in PowerShell because reading a name out of a line and testing a path is a regular
rem  expression and an if, and in batch it is neither.
rem
rem  Written without a single pipe, a \" escape or a [^...] class, none of which survive being
rem  continued across lines with ^ here: cmd treats the \" as the end of the argument and then
rem  reads the | after it as a pipe of its own, so the whole check fell over with
rem  "'Select-Object' is not recognized" and the build sailed past the one thing it exists to
rem  prove. Indexing replaces the first pipe, .Name the second, and .*? the character class.
powershell -NoProfile -Command ^
  "$root = Join-Path $env:PAGES 'wwwroot';" ^
  "$found = (Select-String -Path (Join-Path $root 'index.html') -Pattern '_framework/blazor\.webassembly.*?\.js' -AllMatches).Matches;" ^
  "if (-not $found) { Write-Host 'FAILED: index.html names no Blazor script at all.'; exit 1 };" ^
  "$asked = $found[0].Value;" ^
  "if (-not (Test-Path (Join-Path $root $asked))) {" ^
  "  Write-Host ('FAILED: index.html asks for ' + $asked + ', which was not published. The page would never start.');" ^
  "  (Get-ChildItem (Join-Path $root '_framework') -Filter 'blazor.webassembly*').Name;" ^
  "  exit 1 };" ^
  "Write-Host ('index.html asks for ' + $asked + ', which is there.')"
if errorlevel 1 goto :failed

rem  The loader asks for this one by name, flatly - nothing can redirect it to a hashed file the
rem  way the dev server does, so fingerprinting it gives a site that loads and never starts.
if not exist "%PAGES%\wwwroot\_framework\dotnet.js" (
    echo.
    echo FAILED: _framework\dotnet.js was not published under that name. The platform would never start.
    dir /b "%PAGES%\wwwroot\_framework\dotnet*"
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
