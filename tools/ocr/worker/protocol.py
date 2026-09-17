import json
from typing import Any, Dict, Optional

PROTOCOL_VERSION = 1


def create_response(request_id: str, success: bool, result: Optional[Any] = None, error: Optional[Dict[str, str]] = None) -> str:
    response = {
        "protocolVersion": PROTOCOL_VERSION,
        "requestId": request_id,
        "success": success,
        "result": result,
        "error": error
    }
    return json.dumps(response, ensure_ascii=False)


def parse_request(line: str) -> Dict[str, Any]:
    data = json.loads(line)
    if not isinstance(data, dict):
        raise ValueError("Request must be a JSON object.")
    if "requestId" not in data or "method" not in data:
        raise ValueError("Request must contain 'requestId' and 'method'.")
    return data
