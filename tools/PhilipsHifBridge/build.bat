@echo off
setlocal
title PhilipsHifBridge Build
set "DIR=%~dp0"
set "ROOT=%DIR%..\.."
set "OUT=%DIR%bin\Release"
set "LOG=%DIR%build.log"
set "MSBUILD_EXE="
set "TMP_MSBUILD=%TEMP%\philips-hif-msbuild.txt"

echo ==== PhilipsHifBridge build %date% %time% ==== > "%LOG%"
echo DIR=%DIR% >> "%LOG%"
echo ROOT=%ROOT% >> "%LOG%"

where msbuild > "%TMP_MSBUILD%" 2>nul
if not errorlevel 1 goto read_msbuild_from_where
goto try_vswhere

:read_msbuild_from_where
set /p MSBUILD_EXE=<"%TMP_MSBUILD%"
goto found_msbuild

:try_vswhere
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" goto found_msbuild
"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" > "%TMP_MSBUILD%" 2>nul
set /p MSBUILD_EXE=<"%TMP_MSBUILD%"
goto found_msbuild

:found_msbuild
if "%MSBUILD_EXE%"=="" goto msbuild_missing
if not exist "%ROOT%\dll_NEW\Philips.PlatformServices.dll" goto dll_missing

echo MSBuild=%MSBUILD_EXE%
echo MSBuild=%MSBUILD_EXE% >> "%LOG%"

"%MSBUILD_EXE%" "%DIR%PhilipsHifBridge.csproj" /p:Configuration=Release >> "%LOG%" 2>&1
if errorlevel 1 goto build_failed

echo Copying Philips DLL dependencies...
echo Copying Philips DLL dependencies... >> "%LOG%"
for %%D in (
  Philips.PlatformServices.dll
  Philips.Platform.dll
  BouncyCastle.Crypto.dll
  Microsoft.Web.Administration.dll
  Interop.NetFwTypeLib.dll
  Interop.IWshRuntimeLibrary.dll
  Microsoft.SqlServer.Smo.dll
  Microsoft.SqlServer.SqlEnum.dll
  Microsoft.SqlServer.ConnectionInfo.dll
  Microsoft.SqlServer.SmoExtended.dll
  Microsoft.SqlServer.Management.Sdk.Sfc.dll
  Microsoft.SqlServer.RegSvrEnum.dll
) do (
  if exist "%ROOT%\dll_NEW\%%D" copy /Y "%ROOT%\dll_NEW\%%D" "%OUT%\" >> "%LOG%" 2>&1
)
if not exist "%OUT%\Philips.PlatformServices.dll" goto copy_failed

if exist "%OUT%\bridge.log" del /Q "%OUT%\bridge.log" >> "%LOG%" 2>&1

echo.
echo Build complete: %OUT%\PhilipsHifBridge.exe
echo Build complete: %OUT%\PhilipsHifBridge.exe >> "%LOG%"
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 0

:msbuild_missing
echo ERROR: msbuild not found.
echo ERROR: msbuild not found. >> "%LOG%"
echo.
echo Please install Visual Studio 2022 with ".NET desktop development",
echo or run this from Developer Command Prompt.
echo.
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:dll_missing
echo ERROR: Missing new Philips DLL folder.
echo Expected: %ROOT%\dll_NEW
echo ERROR: Missing new Philips DLL folder: %ROOT%\dll_NEW >> "%LOG%"
echo.
echo Tip: unzip the whole PhilipsHifBridgeSource.zip, not only the tools folder.
echo.
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:build_failed
echo ERROR: Build failed. See:
echo %LOG%
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1

:copy_failed
echo ERROR: Failed to copy Philips DLL dependencies. See:
echo %LOG%
if not defined HL7GATEWAY_NO_PAUSE pause
exit /b 1
