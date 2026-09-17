import logging
import sys
import traceback

from protocol import create_response, parse_request
from paddle_engine import PaddleOcrEngine

# Ensure UTF-8 stdio on Windows
if hasattr(sys.stdin, "reconfigure"):
    sys.stdin.reconfigure(encoding="utf-8")
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

# Redirect Python logging to sys.stderr so stdout remains exclusively for protocol JSON lines
logging.basicConfig(
    stream=sys.stderr,
    level=logging.INFO,
    format="[%(asctime)s] %(levelname)s: %(message)s"
)


def main():
    engine = PaddleOcrEngine()

    while True:
        try:
            line = sys.stdin.readline()
            if not line:
                break

            line = line.strip()
            if not line:
                continue

            try:
                request = parse_request(line)
            except Exception as parse_err:
                sys.stderr.write(f"Malformed request line: {parse_err}\n")
                sys.stderr.flush()
                response_json = create_response(
                    request_id="unknown",
                    success=False,
                    error={"code": "InvalidJson", "message": str(parse_err)}
                )
                sys.stdout.write(response_json + "\n")
                sys.stdout.flush()
                continue

            request_id = request["requestId"]
            method = request["method"]
            payload = request.get("payload") or {}

            try:
                if method == "health":
                    result = engine.get_health()
                    response_json = create_response(request_id, success=True, result=result)

                elif method == "initialize":
                    engine.initialize()
                    result = engine.get_health()
                    result["initialized"] = True
                    response_json = create_response(request_id, success=True, result=result)

                elif method == "ocr_page":
                    image_path = payload.get("imagePath", "")
                    tile_threshold = payload.get("tileThreshold")
                    tile_height = payload.get("tileHeight")
                    tile_overlap = payload.get("tileOverlap")

                    result = engine.ocr_page(
                        image_path=image_path,
                        tile_threshold=tile_threshold,
                        tile_height=tile_height,
                        tile_overlap=tile_overlap
                    )
                    response_json = create_response(request_id, success=True, result=result)

                elif method == "ocr_region":
                    image_path = payload.get("imagePath", "")
                    points = payload.get("points") or payload.get("polygon") or []

                    result = engine.ocr_region(image_path=image_path, points=points)
                    response_json = create_response(request_id, success=True, result=result)

                elif method == "shutdown":
                    response_json = create_response(request_id, success=True, result={"status": "shutting_down"})
                    sys.stdout.write(response_json + "\n")
                    sys.stdout.flush()
                    break

                else:
                    response_json = create_response(
                        request_id,
                        success=False,
                        error={"code": "UnknownMethod", "message": f"Method '{method}' is not supported."}
                    )

            except Exception as exec_err:
                sys.stderr.write(f"Error handling method '{method}': {exec_err}\n")
                traceback.print_exc(file=sys.stderr)
                sys.stderr.flush()

                error_code = type(exec_err).__name__
                response_json = create_response(
                    request_id,
                    success=False,
                    error={"code": error_code, "message": str(exec_err)}
                )

            sys.stdout.write(response_json + "\n")
            sys.stdout.flush()

        except KeyboardInterrupt:
            break
        except Exception as top_err:
            sys.stderr.write(f"Top-level worker error: {top_err}\n")
            sys.stderr.flush()
            break

    sys.stderr.write("OCR Worker exiting cleanly.\n")
    sys.stderr.flush()


if __name__ == "__main__":
    main()
