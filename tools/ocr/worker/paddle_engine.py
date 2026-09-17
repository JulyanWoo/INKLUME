import math
import os
import sys
from typing import Any, Dict, List, Optional, Tuple
import numpy as np
from PIL import Image

try:
    import paddle
    import paddleocr
    from paddleocr import PaddleOCR
    PADDLE_AVAILABLE = True
except ImportError:
    paddle = None
    paddleocr = None
    PaddleOCR = None
    PADDLE_AVAILABLE = False


def calculate_iou(box1: Tuple[float, float, float, float], box2: Tuple[float, float, float, float]) -> float:
    # box is (min_x, min_y, max_x, max_y)
    x1 = max(box1[0], box2[0])
    y1 = max(box1[1], box2[1])
    x2 = min(box1[2], box2[2])
    y2 = min(box1[3], box2[3])

    intersection = max(0.0, x2 - x1) * max(0.0, y2 - y1)
    if intersection <= 0.0:
        return 0.0

    area1 = (box1[2] - box1[0]) * (box1[3] - box1[1])
    area2 = (box2[2] - box2[0]) * (box2[3] - box2[1])
    union = area1 + area2 - intersection

    return intersection / union if union > 0.0 else 0.0


class PaddleOcrEngine:
    def __init__(self):
        self.ocr: Optional[Any] = None
        self.detector_model = "PP-OCRv5_mobile_det"
        self.recognition_model = "korean_PP-OCRv5_mobile_rec"
        self.device = "cpu"
        self.model_profile = "Korean + English"

    def get_health(self) -> Dict[str, Any]:
        python_ver = f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
        p_version = paddle.__version__ if PADDLE_AVAILABLE and paddle else "Unavailable"
        ocr_version = paddleocr.__version__ if PADDLE_AVAILABLE and paddleocr else "Unavailable"

        return {
            "pythonVersion": python_ver,
            "paddleOcrVersion": ocr_version,
            "paddlePaddleVersion": p_version,
            "device": self.device,
            "detectorModel": self.detector_model,
            "recognitionModel": self.recognition_model,
            "modelProfile": self.model_profile,
            "modelsAvailable": PADDLE_AVAILABLE
        }

    def initialize(self):
        if not PADDLE_AVAILABLE:
            raise RuntimeError("PaddleOCR or PaddlePaddle is not installed in the active environment.")

        if self.ocr is not None:
            return

        sys.stderr.write("Initializing PaddleOCR models (CPU)...\n")
        sys.stderr.flush()

        # Disable paddle telemetry / verbose stdout
        os.environ["PADDLE_PDX_DISABLE_LOG"] = "1"
        os.environ["PADDLE_DISABLE_TELEMETRY"] = "1"

        try:
            # Modern PaddleOCR 3.x API (enable_mkldnn=False required on CPU for Paddle 3.3.1)
            self.ocr = PaddleOCR(
                text_detection_model_name=self.detector_model,
                text_recognition_model_name=self.recognition_model,
                use_doc_orientation_classify=False,
                use_doc_unwarping=False,
                use_textline_orientation=False,
                device=self.device,
                enable_mkldnn=False
            )
        except TypeError:
            # Fallback for standard multilingual parameter
            self.ocr = PaddleOCR(
                lang="korean",
                use_angle_cls=False,
                device=self.device,
                enable_mkldnn=False
            )

        sys.stderr.write("PaddleOCR models initialized successfully.\n")
        sys.stderr.flush()

    def ocr_page(
        self,
        image_path: str,
        tile_threshold: Optional[int] = 4000,
        tile_height: Optional[int] = 3000,
        tile_overlap: Optional[int] = 500
    ) -> Dict[str, Any]:
        self.initialize()

        if not os.path.isfile(image_path):
            raise FileNotFoundError(f"Image file not found: {image_path}")

        threshold = tile_threshold or 4000
        t_height = tile_height or 3000
        t_overlap = tile_overlap or 500

        with Image.open(image_path) as img:
            width, height = img.size

            if height <= threshold:
                detections = self._run_inference_on_image(image_path, offset_y=0.0)
            else:
                detections = self._run_tiled_inference(img, width, height, t_height, t_overlap)

        return {
            "regions": detections,
            "engineName": "PaddleOCR",
            "engineVersion": paddleocr.__version__ if PADDLE_AVAILABLE else "3.7.0",
            "detectionModel": self.detector_model,
            "recognitionModel": self.recognition_model,
            "modelProfile": self.model_profile
        }

    def ocr_region(self, image_path: str, points: List[Dict[str, float]]) -> Dict[str, Any]:
        self.initialize()

        if not os.path.isfile(image_path):
            raise FileNotFoundError(f"Image file not found: {image_path}")

        if not points:
            raise ValueError("No points provided for region OCR.")

        with Image.open(image_path) as img:
            width, height = img.size

            xs = [p["x"] for p in points]
            ys = [p["y"] for p in points]

            min_x = max(0, int(math.floor(min(xs))))
            min_y = max(0, int(math.floor(min(ys))))
            max_x = min(width, int(math.ceil(max(xs))))
            max_y = min(height, int(math.ceil(max(ys))))

            if max_x <= min_x or max_y <= min_y:
                raise ValueError("Region geometry has zero or negative extent.")

            cropped = img.crop((min_x, min_y, max_x, max_y))
            np_crop = np.array(cropped.convert("RGB"))

            recognized_text = ""
            confidence = 0.0

            # Use internal text_rec_model if available in PaddleX pipeline
            internal = getattr(self.ocr, "paddlex_pipeline", None)
            if internal and hasattr(internal, "_pipeline") and hasattr(internal._pipeline, "text_rec_model"):
                rec_res = list(internal._pipeline.text_rec_model(np_crop))
                if rec_res:
                    first = rec_res[0]
                    recognized_text = str(first.get("rec_text", "")).strip()
                    confidence = float(first.get("rec_score", 0.0))
            else:
                # Fallback to full predict on cropped array
                pred = list(self.ocr.predict(np_crop))
                if pred and isinstance(pred[0], dict):
                    texts = pred[0].get("rec_texts", [])
                    scores = pred[0].get("rec_scores", [])
                    recognized_text = " ".join(texts).strip()
                    confidence = float(scores[0]) if scores else 0.0

        return {
            "text": recognized_text,
            "recognitionConfidence": max(0.0, min(1.0, confidence)),
            "engineName": "PaddleOCR",
            "engineVersion": paddleocr.__version__ if PADDLE_AVAILABLE else "3.7.0",
            "detectionModel": self.detector_model,
            "recognitionModel": self.recognition_model,
            "modelProfile": self.model_profile
        }

    def _run_inference_on_image(self, image_input: Any, offset_y: float = 0.0) -> List[Dict[str, Any]]:
        predictions = list(self.ocr.predict(image_input))
        return self._parse_ocr_result(predictions, offset_y=offset_y)

    def _run_tiled_inference(
        self,
        pil_img: Image.Image,
        width: int,
        height: int,
        tile_height: int,
        tile_overlap: int
    ) -> List[Dict[str, Any]]:
        step = max(100, tile_height - tile_overlap)
        y_starts = list(range(0, height, step))

        all_detections: List[Dict[str, Any]] = []

        for y_start in y_starts:
            y_end = min(height, y_start + tile_height)
            tile = pil_img.crop((0, y_start, width, y_end))
            np_tile = np.array(tile.convert("RGB"))

            tile_detections = self._run_inference_on_image(np_tile, offset_y=float(y_start))
            all_detections.extend(tile_detections)

        # Deduplicate detections near tile overlap boundaries
        return self._deduplicate_detections(all_detections)

    def _parse_ocr_result(self, predictions: Any, offset_y: float = 0.0) -> List[Dict[str, Any]]:
        parsed = []
        if not predictions:
            return parsed

        for page_res in predictions:
            if not page_res:
                continue

            # Modern PaddleOCR 3.x dict format
            if isinstance(page_res, dict):
                polys = page_res.get("dt_polys") or page_res.get("rec_polys") or []
                texts = page_res.get("rec_texts") or []
                scores = page_res.get("rec_scores") or []

                count = min(len(polys), len(texts), len(scores))
                for i in range(count):
                    raw_poly = polys[i]
                    text = str(texts[i]).strip()
                    conf = float(scores[i])

                    pts = []
                    for pt in raw_poly:
                        if len(pt) >= 2:
                            pts.append({
                                "x": float(pt[0]),
                                "y": float(pt[1]) + offset_y
                            })

                    if len(pts) >= 3 and len(text) > 0:
                        parsed.append({
                            "points": pts,
                            "text": text,
                            "recognitionConfidence": max(0.0, min(1.0, conf)),
                            "detectionConfidence": None
                        })
                continue

            # Legacy PaddleOCR 2.x list format
            for line in page_res:
                if not line or len(line) < 2:
                    continue
                poly_coords = line[0]
                rec_info = line[1]
                if not isinstance(rec_info, (list, tuple)) or len(rec_info) < 2:
                    continue
                text = str(rec_info[0]).strip()
                rec_conf = float(rec_info[1])
                pts = []
                for pt in poly_coords:
                    if len(pt) >= 2:
                        pts.append({
                            "x": float(pt[0]),
                            "y": float(pt[1]) + offset_y
                        })
                if len(pts) >= 3 and len(text) > 0:
                    parsed.append({
                        "points": pts,
                        "text": text,
                        "recognitionConfidence": max(0.0, min(1.0, rec_conf)),
                        "detectionConfidence": None
                    })

        return parsed

    def _deduplicate_detections(self, detections: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        if len(detections) <= 1:
            return detections

        # Compute bounding boxes
        boxes = []
        for det in detections:
            pts = det["points"]
            min_x = min(p["x"] for p in pts)
            min_y = min(p["y"] for p in pts)
            max_x = max(p["x"] for p in pts)
            max_y = max(p["y"] for p in pts)
            boxes.append((min_x, min_y, max_x, max_y))

        keep = [True] * len(detections)

        for i in range(len(detections)):
            if not keep[i]:
                continue
            for j in range(i + 1, len(detections)):
                if not keep[j]:
                    continue

                iou = calculate_iou(boxes[i], boxes[j])
                if iou > 0.35:
                    # Duplicate detected in overlap area: keep the one with higher confidence
                    if detections[i]["recognitionConfidence"] >= detections[j]["recognitionConfidence"]:
                        keep[j] = False
                    else:
                        keep[i] = False
                        break

        return [detections[idx] for idx in range(len(detections)) if keep[idx]]
