@echo off
REM Example smoke on BobStudio. Pass a *copy* of a Samples .zmx — never a live client design.
setlocal
set PY=C:\Users\bob\AppData\Local\Programs\Python\Python312\python.exe
set ZEMAX_ROOT=C:\Program Files\Ansys Zemax OpticStudio 2026 R1.01
set SMOKE_DIR=C:\GIT\zue-smoke-out\ElementLeaveOneOut\python_smoke
if not exist "%SMOKE_DIR%" mkdir "%SMOKE_DIR%"
if "%~1"=="" (
  echo Usage: smoke_example.bat path\to\copied_sample.zmx
  exit /b 2
)
"%PY%" "%~dp0element_leave_one_out.py" -file "%~1" -out "%SMOKE_DIR%" -report -cycles 30
echo EXITCODE=%ERRORLEVEL%
