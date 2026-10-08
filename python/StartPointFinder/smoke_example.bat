@echo off
REM Example smoke. Pass a folder of *copied* sample objectives - never your only copies.
REM Optional 3rd/4th arguments: requested F/# and half field (deg), e.g. 4 16.
REM Optional 5th argument: start mode. Default best (every input and the blend are
REM scored at the request, the 3 best-scored starts are optimized, and the lowest
REM merit that passes the checks wins). Pass nearest or blend to force one.
REM Optional 6th argument: layout fingerprint to use instead of the biggest group.
REM Extra flags go in the EXTRA variable, e.g. set EXTRA=-top all   or   set EXTRA=-hammer 20
setlocal
if "%PY%"=="" set PY=python
if "%ZEMAX_ROOT%"=="" set ZEMAX_ROOT=C:\Program Files\Ansys Zemax OpticStudio 2026 R1.01
if "%~1"=="" (
  echo Usage: smoke_example.bat path\to\folder_of_copied_samples [out_dir] [fno] [hfov_deg] [best|nearest|blend] [layout]
  exit /b 2
)
set OUT=%~2
if "%OUT%"=="" set OUT=%~1\_StartPointFinder
set REQ=
if not "%~3"=="" set REQ=-fno %~3
if not "%~4"=="" set REQ=%REQ% -hfov %~4
if not "%~5"=="" set REQ=%REQ% -start %~5
if not "%~6"=="" set REQ=%REQ% -layout %~6
"%PY%" "%~dp0start_point_finder.py" -dir "%~1" -out "%OUT%" %REQ% %EXTRA% -force -quiet
echo EXITCODE=%ERRORLEVEL%
