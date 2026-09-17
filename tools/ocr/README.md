# INKLUME OCR Worker

This directory contains the Python worker and configuration for local OCR inference in INKLUME.

## Technology Stack
- **Python**: 3.11 x64 preferred (3.12 x64 also compatible)
- **PaddleOCR**: 3.7.0
- **PaddlePaddle**: 3.3.1 (CPU baseline)
- **Detector**: `PP-OCRv5_mobile_det`
- **Recognizer**: `korean_PP-OCRv5_mobile_rec`
- **Language Profile**: Korean + English + numeric text

## Protocol
Communication between INKLUME (.NET) and the worker uses JSON Lines (NDJSON) over standard I/O:
- `stdin`: Requests from INKLUME
- `stdout`: Protocol JSON responses only (one JSON object per line)
- `stderr`: Framework logs and diagnostic traces

### Protocol Methods
1. `health`: Reports Python, Paddle, model versions, and availability.
2. `initialize`: Preloads models into memory (one-time cost).
3. `ocr_page`: Runs text detection and recognition on a canonical source image. Supports vertical tiling for tall manhwa pages.
4. `ocr_region`: Performs in-memory cropped recognition on specified region points.
5. `shutdown`: Gracefully terminates the worker.

## Offline Rule
Normal `.NET` unit tests (`dotnet test`) never invoke this worker and never load Paddle models.
Integration with the real worker is opt-in via:
- `scripts/Setup-Ocr.ps1`
- `scripts/Smoke-Ocr.ps1`
