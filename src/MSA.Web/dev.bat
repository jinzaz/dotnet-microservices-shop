@echo off
setlocal EnableDelayedExpansion

rem Starts frontend dev servers, each in its own window.
rem   dev.bat                 start all (bm, identity, front)
rem   dev.bat bm front        start only the listed apps
rem   dev.bat help            show usage
rem Apps: bm (http://localhost:5175)  identity (http://localhost:5174)  front (http://localhost:5173)

set "ROOT=%~dp0"
set "APPS=bm identity front"

if /i "%~1"=="help" goto :usage
if /i "%~1"=="-h" goto :usage
if /i "%~1"=="/?" goto :usage

set "SELECTED=%*"
if "%SELECTED%"=="" set "SELECTED=%APPS%"

rem Validate every argument before starting anything.
for %%A in (%SELECTED%) do (
  set "KNOWN="
  for %%K in (%APPS%) do if /i "%%A"=="%%K" set "KNOWN=1"
  if not defined KNOWN (
    echo Unknown app: %%A
    goto :usage_error
  )
)

where npm >nul 2>nul || (
  echo npm was not found on PATH. Install Node.js first.
  exit /b 1
)

for %%A in (%SELECTED%) do call :start %%A
echo.
echo Started: %SELECTED%
echo Close an app's window, or press Ctrl+C in it, to stop that dev server.
exit /b 0

:start
set "NAME=%~1"
if /i "%NAME%"=="bm" (set "DIR=BM" & set "PORT=5175")
if /i "%NAME%"=="identity" (set "DIR=Identity" & set "PORT=5174")
if /i "%NAME%"=="front" (set "DIR=Front" & set "PORT=5173")

if not exist "%ROOT%%DIR%\package.json" (
  echo Skipped %NAME%: %ROOT%%DIR%\package.json not found.
  exit /b 0
)

rem Install dependencies on first run so a fresh clone works in one step.
set "INSTALL="
if not exist "%ROOT%%DIR%\node_modules" set "INSTALL=npm install && "

echo Starting %NAME% on http://localhost:%PORT% ...
start "MSA %DIR% - :%PORT%" /D "%ROOT%%DIR%" cmd /k "%INSTALL%npm run dev"
exit /b 0

:usage
echo Usage: dev.bat [bm] [identity] [front]
echo   No arguments starts all apps. Names are case-insensitive.
echo   bm        MSA.Web\BM        http://localhost:5175
echo   identity  MSA.Web\Identity  http://localhost:5174
echo   front     MSA.Web\Front     http://localhost:5173
exit /b 0

:usage_error
echo.
echo Usage: dev.bat [bm] [identity] [front]
exit /b 1
