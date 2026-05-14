@echo off
cd /d "%~dp0"

echo Starting Fuel Audit Control Centre V37...
echo.

python --version >nul 2>&1
if errorlevel 1 (
    echo Python was not found.
    echo Install Python from https://www.python.org/downloads/
    echo IMPORTANT: tick "Add Python to PATH" during install.
    pause
    exit /b
)

python -m pip install -r requirements.txt

echo.
echo Opening browser...
start "" "http://localhost:8501"
echo If browser opens too early, wait 10 seconds then refresh.
echo.

python -m streamlit run app.py --server.address localhost --server.port 8501

pause
