# INKLUME OCR Opt-in Real Smoke Test
# Tests real PaddleOCR inference on synthetic copyright-safe test fixtures.
# This script is strictly OPT-IN and is NEVER executed by dotnet test.

[CmdletBinding()]
param(
    [string]$PythonPath = ""
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " INKLUME Real OCR Smoke Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$RepoRoot = Split-Path -Parent $PSScriptRoot
$LocalOcrDir = Join-Path $RepoRoot ".local\ocr"
$VenvPython = Join-Path $LocalOcrDir ".venv\Scripts\python.exe"
$WorkerScript = Join-Path $RepoRoot "tools\ocr\worker\main.py"

if (-not (Test-Path $VenvPython)) {
    Write-Host "Local virtual environment not found. Running Setup-Ocr.ps1..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "Setup-Ocr.ps1") -PythonPath $PythonPath
    if (-not (Test-Path $VenvPython)) {
        throw "Python virtual environment could not be initialized."
    }
}

$FixturesDir = Join-Path $env:TEMP ("inklume-ocr-smoke-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $FixturesDir -Force | Out-Null

try {
    # 1. Generate Synthetic Fixtures
    Write-Host "Generating synthetic test fixtures in $FixturesDir..." -ForegroundColor Cyan

    $generateScript = @"
import os
import sys
from PIL import Image, ImageDraw, ImageFont

fixtures_dir = r'$FixturesDir'

# Load high-visibility system fonts if available
font_en = None
for ef in ['arial.ttf', 'calibri.ttf', 'segoeui.ttf']:
    fpath = os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts', ef)
    if os.path.exists(fpath):
        try:
            font_en = ImageFont.truetype(fpath, 32)
            break
        except Exception:
            pass

# 1. English fixture (800 x 400)
img_en = Image.new('RGB', (800, 400), color=(255, 255, 255))
draw_en = ImageDraw.Draw(img_en)
draw_en.text((50, 50), 'CHAPTER 1: THE BEGINNING', fill=(0, 0, 0), font=font_en)
draw_en.text((50, 150), 'WELCOME TO INKLUME OCR SYSTEM', fill=(0, 0, 0), font=font_en)
draw_en.text((50, 250), 'TESTING NUMERIC 1234567890', fill=(0, 0, 0), font=font_en)
en_path = os.path.join(fixtures_dir, 'english_fixture.png')
img_en.save(en_path)

# 2. Korean + English fixture (800 x 400)
img_kr = Image.new('RGB', (800, 400), color=(255, 255, 255))
draw_kr = ImageDraw.Draw(img_kr)

korean_fonts = ['malgun.ttf', 'batang.ttc', 'gulim.ttc']
font_kr = None
for kf in korean_fonts:
    fpath = os.path.join(os.environ.get('WINDIR', r'C:\Windows'), 'Fonts', kf)
    if os.path.exists(fpath):
        try:
            font_kr = ImageFont.truetype(fpath, 32)
            break
        except Exception:
            pass

if font_kr is not None:
    draw_kr.text((50, 60), '안녕하세요 인클룸', fill=(0, 0, 0), font=font_kr)
    draw_kr.text((50, 160), 'INKLUME 만화 번역 시스템', fill=(0, 0, 0), font=font_kr)
    draw_kr.text((50, 260), 'KOREAN AND ENGLISH 2026', fill=(0, 0, 0), font=font_kr)
    print('Korean font loaded successfully:', font_kr.path)
else:
    draw_kr.text((50, 60), 'HELLO INKLUME (NO KOREAN FONT)', fill=(0, 0, 0), font=font_en)
    print('Warning: No Korean font found on system; generated with fallback font.')

kr_path = os.path.join(fixtures_dir, 'korean_fixture.png')
img_kr.save(kr_path)

# 3. Long Manhwa Strip fixture (1080 x 5000)
img_long = Image.new('RGB', (1080, 5000), color=(255, 255, 255))
draw_long = ImageDraw.Draw(img_long)
draw_long.text((100, 200), 'TOP BANNER HEADING', fill=(0, 0, 0), font=font_en)
draw_long.text((100, 2500), 'MIDDLE CHAPTER DIALOGUE', fill=(0, 0, 0), font=font_en)
draw_long.text((100, 4800), 'BOTTOM OUTRO CREDITS', fill=(0, 0, 0), font=font_en)
long_path = os.path.join(fixtures_dir, 'long_fixture.png')
img_long.save(long_path)

print('Synthetic fixtures created successfully.')
"@

    & $VenvPython -c $generateScript
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to generate synthetic fixtures."
    }

    $EnglishFixture = Join-Path $FixturesDir "english_fixture.png"
    $KoreanFixture = Join-Path $FixturesDir "korean_fixture.png"
    $LongFixture = Join-Path $FixturesDir "long_fixture.png"

    # Compute Initial File Hashes
    $HashEnBefore = (Get-FileHash $EnglishFixture -Algorithm SHA256).Hash
    $HashKrBefore = (Get-FileHash $KoreanFixture -Algorithm SHA256).Hash
    $HashLongBefore = (Get-FileHash $LongFixture -Algorithm SHA256).Hash

    # 2. Start Worker Process
    Write-Host "Starting OCR Python Worker process..." -ForegroundColor Cyan
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $VenvPython
    $psi.Arguments = "-u `"$WorkerScript`""
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $psi.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::Start($psi)
    if ($null -eq $process -or $process.HasExited) {
        throw "Failed to start OCR worker process."
    }

    # Asynchronous stderr capture via background runspace/job or thread
    $stderrLogs = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
    Register-ObjectEvent -InputObject $process -EventName "ErrorDataReceived" -Action {
        if ($EventArgs.Data) {
            $Event.MessageData.Add($EventArgs.Data) | Out-Null
        }
    } -MessageData $stderrLogs | Out-Null
    $process.BeginErrorReadLine()

    function Send-WorkerRequest {
        param([string]$Method, [hashtable]$Payload = @{})

        $reqId = [Guid]::NewGuid().ToString()
        $req = @{
            protocolVersion = 1
            requestId = $reqId
            method = $Method
            payload = $Payload
        }

        $json = $req | ConvertTo-Json -Compress -Depth 10
        $process.StandardInput.WriteLine($json)
        $process.StandardInput.Flush()

        $responseLine = $process.StandardOutput.ReadLine()
        if ([string]::IsNullOrWhiteSpace($responseLine)) {
            throw "Worker stdout closed or returned empty line. Last stderr: $($stderrLogs -join "`n")"
        }

        $response = $responseLine | ConvertFrom-Json
        if (-not $response.success) {
            throw "Worker returned error for $($Method): $($response.error.message)"
        }

        return $response.result
    }

    # Step 1: Health
    Write-Host "`n--- Checking Worker Health ---" -ForegroundColor Cyan
    $healthResult = Send-WorkerRequest -Method "health"
    Write-Host "Available: $($healthResult.modelsAvailable)" -ForegroundColor Green
    Write-Host "PaddlePaddle Version: $($healthResult.paddlePaddleVersion)"
    Write-Host "PaddleOCR Version: $($healthResult.paddleOcrVersion)"
    Write-Host "Device: $($healthResult.device)"

    # Step 2: Initialize
    Write-Host "`n--- Initializing PaddleOCR Engine ---" -ForegroundColor Cyan
    $initSw = [System.Diagnostics.Stopwatch]::StartNew()
    $initResult = Send-WorkerRequest -Method "initialize"
    $initSw.Stop()
    $initTimeMs = $initSw.ElapsedMilliseconds
    Write-Host "Engine initialized in $initTimeMs ms" -ForegroundColor Green
    Write-Host "Detection Model: $($initResult.detectionModel)"
    Write-Host "Recognition Model: $($initResult.recognitionModel)"
    Write-Host "Model Profile: $($initResult.modelProfile)"

    # Step 3: English Page OCR (Request 1 - First inference)
    Write-Host "`n--- Testing English Page OCR (Inference 1) ---" -ForegroundColor Cyan
    $inf1Sw = [System.Diagnostics.Stopwatch]::StartNew()
    $enResult = Send-WorkerRequest -Method "ocr_page" -Payload @{ imagePath = $EnglishFixture }
    $inf1Sw.Stop()
    $firstInferenceMs = $inf1Sw.ElapsedMilliseconds
    Write-Host "English OCR completed in $firstInferenceMs ms" -ForegroundColor Green
    Write-Host "Detected regions: $($enResult.regions.Count)"
    foreach ($r in $enResult.regions) {
        Write-Host "  [$($r.recognitionConfidence.ToString('P1'))] $($r.text)"
    }
    if ($enResult.regions.Count -eq 0) {
        throw "English fixture expected at least 1 detection."
    }

    # Step 4: Korean Page OCR (Request 2 - Reused worker)
    Write-Host "`n--- Testing Korean Page OCR (Inference 2 - Worker Reuse) ---" -ForegroundColor Cyan
    $inf2Sw = [System.Diagnostics.Stopwatch]::StartNew()
    $krResult = Send-WorkerRequest -Method "ocr_page" -Payload @{ imagePath = $KoreanFixture }
    $inf2Sw.Stop()
    $secondInferenceMs = $inf2Sw.ElapsedMilliseconds
    Write-Host "Korean OCR completed in $secondInferenceMs ms" -ForegroundColor Green
    Write-Host "Detected regions: $($krResult.regions.Count)"
    foreach ($r in $krResult.regions) {
        Write-Host "  [$($r.recognitionConfidence.ToString('P1'))] $($r.text)"
    }

    # Step 5: Long Manhwa Strip OCR (Height: 5000px, testing tiling/coordinates)
    Write-Host "`n--- Testing Long Manhwa Strip OCR (5000px height) ---" -ForegroundColor Cyan
    $longSw = [System.Diagnostics.Stopwatch]::StartNew()
    $longResult = Send-WorkerRequest -Method "ocr_page" -Payload @{ imagePath = $LongFixture }
    $longSw.Stop()
    $longInferenceMs = $longSw.ElapsedMilliseconds
    Write-Host "Long Strip OCR completed in $longInferenceMs ms" -ForegroundColor Green
    Write-Host "Detected regions on long strip: $($longResult.regions.Count)"
    foreach ($r in $longResult.regions) {
        $minY = ($r.points | Measure-Object -Property y -Minimum).Minimum
        $maxY = ($r.points | Measure-Object -Property y -Maximum).Maximum
        Write-Host "  Y: [$minY .. $maxY] -> $($r.text)"
    }

    # Step 6: Region OCR (Targeted recognition on a crop)
    Write-Host "`n--- Testing Region OCR ---" -ForegroundColor Cyan
    $regResult = Send-WorkerRequest -Method "ocr_region" -Payload @{
        imagePath = $EnglishFixture
        points = @(
            @{ x = 40; y = 40 },
            @{ x = 400; y = 40 },
            @{ x = 400; y = 100 },
            @{ x = 40; y = 100 }
        )
    }
    Write-Host "Region recognized text: $($regResult.text) [Conf: $($regResult.recognitionConfidence.ToString('P1'))]" -ForegroundColor Green

    # Step 7: Verify Source Immutability (Hashes before and after OCR)
    Write-Host "`n--- Verifying Source Immutability ---" -ForegroundColor Cyan
    $HashEnAfter = (Get-FileHash $EnglishFixture -Algorithm SHA256).Hash
    $HashKrAfter = (Get-FileHash $KoreanFixture -Algorithm SHA256).Hash
    $HashLongAfter = (Get-FileHash $LongFixture -Algorithm SHA256).Hash

    if ($HashEnBefore -ne $HashEnAfter -or $HashKrBefore -ne $HashKrAfter -or $HashLongBefore -ne $HashLongAfter) {
        throw "CRITICAL VIOLATION: Source images were modified during OCR!"
    }
    Write-Host "All source image hashes MATCH exactly. (Folder-First invariant verified)." -ForegroundColor Green

    # Step 8: Graceful Shutdown
    Write-Host "`n--- Testing Graceful Worker Shutdown ---" -ForegroundColor Cyan
    $shutdownResult = Send-WorkerRequest -Method "shutdown"
    Write-Host "Worker shutdown acknowledged: $($shutdownResult.status)" -ForegroundColor Green

    $process.WaitForExit(5000)
    if (-not $process.HasExited) {
        Write-Host "Worker did not exit within 5s, terminating..." -ForegroundColor Yellow
        $process.Kill()
    } else {
        Write-Host "Worker process exited cleanly with code $($process.ExitCode)." -ForegroundColor Green
    }

    # Final Summary
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host " REAL OCR SMOKE TEST PASSED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Engine Init Time:       $initTimeMs ms"
    Write-Host "First Inference Time:   $firstInferenceMs ms"
    Write-Host "Second Inference Time:  $secondInferenceMs ms (Worker reused)"
    Write-Host "Long Strip Time:        $longInferenceMs ms"
    Write-Host "Source Hash Check:      PASS (Unmodified)"
    Write-Host "========================================" -ForegroundColor Cyan

} finally {
    if ($process -and -not $process.HasExited) {
        try { $process.Kill() } catch { }
    }
    if (Test-Path $FixturesDir) {
        try { Remove-Item -Path $FixturesDir -Recurse -Force } catch { }
    }
}
