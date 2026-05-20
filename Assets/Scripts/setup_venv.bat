@echo off
SET PYTHON39=C:\Users\Maild\AppData\Local\Programs\Python\Python39\python.exe
SET VENV_DIR=%~dp0venv

if exist "%VENV_DIR%\Scripts\python.exe" (
    "%VENV_DIR%\Scripts\python.exe" -c "import numpy" >nul 2>&1
    if not errorlevel 1 (
        echo [mono] Python venv is healthy, skipping setup.
        exit /b 0
    )
    echo [mono] Venv exists but packages missing — recreating...
    rmdir /s /q "%VENV_DIR%"
)

echo [mono] Creating Python 3.9 venv for audio analysis...
"%PYTHON39%" -m venv "%VENV_DIR%"
if errorlevel 1 (
    echo [mono] ERROR: Failed to create venv.
    exit /b 1
)
"%VENV_DIR%\Scripts\pip.exe" install --upgrade pip setuptools^<75.0.0
"%VENV_DIR%\Scripts\pip.exe" install ^
    "librosa==0.10.2" ^
    "numpy==1.23.5" ^
    "scipy==1.13.1" ^
    "cython==3.2.4" ^
    "mido==1.3.3" ^
    "six==1.17.0" ^
    "pyyaml==6.0.3" ^
    "importlib-metadata==8.0.0" ^
    "madmom==0.16.1"
echo [mono] Venv setup complete.
