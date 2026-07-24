@echo off
REM ============================================================
REM Quantivus OMP / VSAgent  - post-install registry repair
REM
REM VSIXInstaller occasionally leaves the CodeBase / InprocServer32
REM missing for VS 2026 Experimental. This script rewrites those
REM keys against the freshly installed extension folder.
REM
REM MUST be run as Administrator (right-click -> "Run as admin").
REM ============================================================

setlocal

set "HASH=z2h5qxvq.dhx"
set "GUID={41f0c0d8-5c2b-4f02-8c7e-0b3d3b6e1a2f}"
set "VSID=18.0_719c975fExp"
set "USERPROFILE_DIR=%USERPROFILE%"
set "PRIVBIN=%USERPROFILE_DIR%\AppData\Local\Microsoft\VisualStudio\%VSID%\privateregistry.bin"
set "DLLPATH=%USERPROFILE_DIR%\AppData\Local\Microsoft\VisualStudio\%VSID%\Extensions\%HASH%\VSAgent.dll"
set "PKGKEY=HKU\Tmp\Software\Microsoft\VisualStudio\%VSID%_Config\Packages\%GUID%"
set "SATKEY=HKU\Tmp\Software\Microsoft\VisualStudio\%VSID%_Config\SatelliteDlls\%GUID%"

echo === Quantivus OMP registry repair ===
echo HASH      : %HASH%
echo DLLPATH   : %DLLPATH%
echo PKGKEY    : %PKGKEY%
echo.

if not exist "%PRIVBIN%" (
    echo [FATAL] privateregistry.bin not found at "%PRIVBIN%"
    exit /b 1
)
if not exist "%DLLPATH%" (
    echo [FATAL] VSAgent.dll not found at "%DLLPATH%"
    echo Did VSIXInstaller use the same hash? Check:
    echo   dir "%USERPROFILE_DIR%\AppData\Local\Microsoft\VisualStudio\%VSID%\Extensions\"
    exit /b 1
)

echo [1/6] Loading privateregistry.bin into HKU\Tmp ...
reg load HKU\Tmp "%PRIVBIN%"
if errorlevel 1 ( echo Failed to load hive & exit /b 1 )

echo [2/6] Writing (Default) = "VSAgent Package" ...
reg add "%PKGKEY%" /ve /d "VSAgent Package" /f

echo [3/6] Writing InprocServer32 = C:\WINDOWS\SYSTEM32\MSCOREE.DLL ...
reg add "%PKGKEY%\InprocServer32" /ve /d "C:\WINDOWS\SYSTEM32\MSCOREE.DLL" /f
echo [3a/6] Writing CodeBase (top-level) = %DLLPATH% ...
reg add "%PKGKEY%" /v CodeBase /d "%DLLPATH%" /f
echo [4/6] Writing Class = VSAgentPackage ...
reg add "%PKGKEY%\InprocServer32" /v Class /d "VSAgentPackage" /f

echo [5/6] Writing CodeBase = %DLLPATH% ...
reg add "%PKGKEY%\InprocServer32" /v CodeBase /d "file:///%DLLPATH:\=/%" /f

echo [6/6] Writing AllowsBackgroundLoad + SatelliteDlls ...
reg add "%PKGKEY%" /v AllowsBackgroundLoad /t REG_DWORD /d 1 /f
reg add "%SATKEY%" /v "1033" /d "%USERPROFILE_DIR%\AppData\Local\Microsoft\VisualStudio\%VSID%\Extensions\%HASH%\Resources\1033\VSAgentUI.dll" /f >nul 2>&1

echo Unloading HKU\Tmp ...
reg unload HKU\Tmp
if errorlevel 1 (
    echo [WARN] Failed to unload HKU\Tmp. Run manually:
    echo    reg unload HKU\Tmp
)

echo.
echo === Done ===
echo Restart Visual Studio Experimental instance. The "Quantivus OMP" tool
echo window should now be available under View -^> Other Windows.
endlocal
