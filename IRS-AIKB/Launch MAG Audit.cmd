@echo off
setlocal
set "APP_DIR=%~dp0"
set "BUNDLED_PY=C:\Users\moabughoush\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
if exist "%BUNDLED_PY%" (
  "%BUNDLED_PY%" "%APP_DIR%launch_mag_audit.py"
  exit /b %errorlevel%
)
where py >nul 2>nul
if %errorlevel%==0 (
  py -3 "%APP_DIR%launch_mag_audit.py"
  exit /b %errorlevel%
)
where python >nul 2>nul
if %errorlevel%==0 (
  python "%APP_DIR%launch_mag_audit.py"
  exit /b %errorlevel%
)
echo Python could not be found. Open MAG Audit from Codex or install Python 3.11 or newer.
pause
