#!/usr/bin/env python3
"""Captivox GPU diarization sidecar probe.

Small, file-based probe used by PowerShell. This intentionally avoids passing
multi-line Python through `python -c`, because that proved brittle on Windows
PowerShell with native command error handling.
"""
from __future__ import annotations

import argparse
import importlib
import json
import sys
from typing import Any

from pyannote_gpu_compat import apply_torchaudio_compatibility


def emit(payload: dict[str, Any], exit_code: int) -> None:
    print(json.dumps(payload, ensure_ascii=False, sort_keys=True))
    raise SystemExit(exit_code)


def check_modules(modules: list[str]) -> None:
    missing: list[str] = []
    errors: dict[str, str] = {}
    for module in modules:
        try:
            if module in {"pyannote.audio", "speechbrain"}:
                apply_torchaudio_compatibility()
            importlib.import_module(module)
        except Exception as exc:  # noqa: BLE001 - diagnostic probe must not hide import errors
            missing.append(module)
            errors[module] = f"{type(exc).__name__}: {exc}"
    emit({"ok": not missing, "missing": missing, "errors": errors}, 0 if not missing else 10)


def check_torch() -> None:
    try:
        import torch  # type: ignore
        payload = {
            "ok": True,
            "torch_version": getattr(torch, "__version__", "unknown"),
            "cuda_available": bool(torch.cuda.is_available()),
            "cuda_device_count": int(torch.cuda.device_count()) if hasattr(torch, "cuda") else 0,
            "torch_cuda_version": getattr(getattr(torch, "version", None), "cuda", None),
            "torch_hip_version": getattr(getattr(torch, "version", None), "hip", None),
        }
        if payload["cuda_available"] and payload["cuda_device_count"]:
            try:
                payload["device_name"] = torch.cuda.get_device_name(0)
            except Exception as exc:  # noqa: BLE001
                payload["device_name_error"] = f"{type(exc).__name__}: {exc}"
        emit(payload, 0)
    except Exception as exc:  # noqa: BLE001
        emit({"ok": False, "error": f"{type(exc).__name__}: {exc}"}, 10)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--check-modules", nargs="+")
    group.add_argument("--check-torch", action="store_true")
    args = parser.parse_args(argv)
    if args.check_modules:
        check_modules(args.check_modules)
    if args.check_torch:
        check_torch()
    return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
