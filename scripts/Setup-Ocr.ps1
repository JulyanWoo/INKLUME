# INKLUME OCR Environment Setup Script
# Sets up a project-local virtual environment for PaddleOCR under .local/ocr/.venv
# Strictly keeps all dependencies isolated without modifying global Python or system PATH.

[CmdletBinding()]
param(
    [string]$PythonPath = "",
    [switch]$Recreate
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " INKLUME OCR Environment Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$RepoRoot = Split-Path -Parent $PSScriptRoot
$LocalOcrDir = Join-Path $RepoRoot ".local\ocr"
$VenvDir = Join-Path $LocalOcrDir ".venv"
$VenvPython = Join-Path $VenvDir "Scripts\python.exe"
$RequirementsFile = Join-Path $RepoRoot "tools\ocr\requirements.txt"

if (-not (Test-Path $RequirementsFile)) {
    throw "Requirements file not found at: $RequirementsFile"
}

# 1. Locate suitable Python executable
function Find-PythonExecutable {
    param([string]$ExplicitPath)

    if ($ExplicitPath -and (Test-Path $ExplicitPath)) {
        return (Resolve-Path $ExplicitPath).Path
    }

    if ($env:INKLUME_PYTHON_PATH -and (Test-Path $env:INKLUME_PYTHON_PATH)) {
        return (Resolve-Path $env:INKLUME_PYTHON_PATH).Path
    }

    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Python\Python311\python.exe",
        "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe",
        "C:\Python311\python.exe",
        "C:\Python312\python.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    # Check if 'python' in PATH is suitable (3.11 or 3.12)
    $pathPython = Get-Command python -ErrorAction SilentlyContinue
    if ($pathPython) {
        $ver = & $pathPython.Source --version 2>&1
        if ($ver -match "Python 3\.(11|12)") {
            return $pathPython.Source
        }
    }

    return $null
}

$BasePython = Find-PythonExecutable -ExplicitPath $PythonPath
if (-not $BasePython) {
    Write-Error "Could not locate a suitable Python 3.11 or 3.12 x64 installation.`nPlease install Python 3.11 or 3.12 x64 or specify -PythonPath 'path\to\python.exe'."
    exit 1
}

Write-Host "Using Base Python: $BasePython" -ForegroundColor Green
$baseVersion = & $BasePython --version 2>&1
Write-Host "Base Python Version: $baseVersion"

# 2. Recreate virtual environment if requested
if ($Recreate -and (Test-Path $VenvDir)) {
    Write-Host "Removing existing virtual environment at $VenvDir..." -ForegroundColor Yellow
    Remove-Item -Path $VenvDir -Recurse -Force
}

# 3. Create virtual environment if missing
if (-not (Test-Path $VenvPython)) {
    Write-Host "Creating project-local virtual environment at $VenvDir..." -ForegroundColor Cyan
    if (-not (Test-Path $LocalOcrDir)) {
        New-Item -ItemType Directory -Path $LocalOcrDir -Force | Out-Null
    }
    & $BasePython -m venv $VenvDir
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create virtual environment at $VenvDir"
    }
}

Write-Host "Virtual Environment: $VenvDir" -ForegroundColor Green
Write-Host "Venv Python: $VenvPython"

# 4. Install / Verify exact dependencies
Write-Host "Installing dependencies from $RequirementsFile..." -ForegroundColor Cyan
& $VenvPython -m pip install --quiet --disable-pip-version-check -r $RequirementsFile
if ($LASTEXITCODE -ne 0) {
    throw "pip install failed with exit code $LASTEXITCODE"
}

# 5. Verify imports and report versions
Write-Host "Verifying OCR imports in virtual environment..." -ForegroundColor Cyan
$verifyScript = @"
import paddle
import paddleocr
import PIL
import numpy
print(f'PaddlePaddle version: {paddle.__version__}')
print(f'PaddleOCR version: {paddleocr.__version__}')
print(f'Pillow version: {PIL.__version__}')
print(f'NumPy version: {numpy.__version__}')
"@

$verifyOutput = & $VenvPython -c $verifyScript
if ($LASTEXITCODE -ne 0) {
    throw "Import verification failed!"
}

Write-Host $verifyOutput -ForegroundColor Green

# 6. Check / prepare global model cache directory
$ModelDir = Join-Path $env:LOCALAPPDATA "INKLUME\Models\OCR"
if (-not (Test-Path $ModelDir)) {
    New-Item -ItemType Directory -Path $ModelDir -Force | Out-Null
}
Write-Host "Model Directory: $ModelDir" -ForegroundColor Green

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " INKLUME OCR Setup Completed Successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
