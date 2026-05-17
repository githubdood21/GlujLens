from __future__ import annotations

import argparse
import shutil
from pathlib import Path

import onnx
from onnxconverter_common import float16


def convert_model(input_path: Path, output_path: Path) -> None:
    model = onnx.load(str(input_path))
    model_fp16 = float16.convert_float_to_float16(
        model,
        keep_io_types=True,
        disable_shape_infer=True,
    )
    output_path.parent.mkdir(parents=True, exist_ok=True)
    onnx.save(model_fp16, str(output_path))


def copy_sidecars(source_dir: Path, output_dir: Path) -> None:
    for pattern in ("*.txt", "*.json", "*.yaml", "*.yml"):
        for path in source_dir.glob(pattern):
            shutil.copy2(path, output_dir / path.name)


def main() -> None:
    parser = argparse.ArgumentParser(description="Convert PaddleOCR ONNX models to FP16.")
    parser.add_argument("source", type=Path, help="Source folder containing FP32 ONNX models.")
    parser.add_argument("output", type=Path, help="Output folder for FP16 ONNX models.")
    parser.add_argument(
        "--include-orientation",
        action="store_true",
        help="Also convert optional text-line orientation ONNX models.",
    )
    args = parser.parse_args()

    source = args.source.resolve()
    output = args.output.resolve()
    if not source.is_dir():
        raise SystemExit(f"Source folder does not exist: {source}")

    model_files = [
        path
        for path in source.glob("*.onnx")
        if "det" in path.name.lower() or "rec" in path.name.lower()
    ]
    if args.include_orientation:
        model_files.extend(
            path
            for path in source.glob("*.onnx")
            if "ori" in path.name.lower() and path not in model_files
        )

    if not model_files:
        raise SystemExit(f"No detection/recognition ONNX files found in {source}")

    output.mkdir(parents=True, exist_ok=True)
    copy_sidecars(source, output)

    for input_path in sorted(model_files):
        output_name = input_path.stem + "_fp16.onnx"
        output_path = output / output_name
        print(f"Converting {input_path.name} -> {output_path.name}")
        convert_model(input_path, output_path)

    print(f"Done: {output}")


if __name__ == "__main__":
    main()
