"""
Build the synchronization state that is sent from Python to Unity.

This file intentionally does not send anything over the network.  It only
answers one question:

    "For the current OCT frame, what state should Unity know about?"

Keeping state construction separate from sending makes the synchronization
logic easier to test and easier to explain.  The sonification code computes
medical/audio values.  This module gives those values a stable JSON shape.
The sender module then transports that JSON shape to Unity.
"""

from __future__ import annotations

import json
import math
from typing import Any, Mapping, Sequence


# Increment this only when the meaning or structure of the JSON payload changes
# in a way that Unity code needs to know about.
PROTOCOL_VERSION = 1


# These are deliberately simple strings because Unity's JsonUtility is happiest
# with plain fields and predictable values.
ALLOWED_WARNING_STATES = {"none", "warning", "critical", "unknown"}


def build_sync_state(
    *,
    frame_index: int,
    timestamp_seconds: float | None = None,
    capture_name: str = "",
    needle_tip: Sequence[float] | Mapping[str, float] | None = None,
    needle_direction: Sequence[float] | Mapping[str, float] | None = None,
    injection_depth: float | None = None,
    injection_depth_unit: str = "voxel",
    injection_angle: float | None = None,
    injection_angle_unit: str = "deg",
    tension_normalized: float | None = None,
    warning_state: str | None = None,
    labels_source: str = "",
    displacement_source: str = "",
    coordinate_space: str = "voxel",
    extra: Mapping[str, Any] | None = None,
) -> dict[str, Any]:
    """
    Create one frame-indexed synchronization payload.

    Parameters are keyword-only on purpose.  It makes call sites more readable:

        build_sync_state(frame_index=i, needle_tip=tip, injection_depth=depth)

    The payload is designed around a simple rule:

        one call to build_sync_state(...) == one OCT frame

    Some values may be unavailable in a given sonification pipeline.  Instead
    of inventing fake data, unavailable values are encoded with
    "available": false.  Unity can then decide whether to hide a visual element,
    keep the previous value, or show a placeholder.
    """

    frame_index = int(frame_index)

    # If no timestamp is provided, use frame_index as a neutral fallback.
    # In the real playback loop it is better to pass frame_index / fps.
    if timestamp_seconds is None:
        timestamp_seconds = float(frame_index)

    safe_warning_state = _normalize_warning_state(warning_state)

    state: dict[str, Any] = {
        "protocol_version": PROTOCOL_VERSION,
        "capture_name": str(capture_name),
        "frame_index": frame_index,
        "timestamp_seconds": _safe_float(timestamp_seconds, default=0.0),
        "coordinate_space": str(coordinate_space),
        "needle": {
            # Current tracker output is often a 2D B-scan point.  When a 2D
            # point is passed, z defaults to the frame index so Unity still gets
            # a simple 3D voxel-like position: (x, y, frame_index).
            "tip_position": _vector_payload(
                needle_tip,
                default_z=float(frame_index),
                space=coordinate_space,
            ),
            # Direction is optional because some pipelines know the tip
            # position before they know a reliable needle orientation.
            "direction": _vector_payload(
                needle_direction,
                default_z=0.0,
                space=coordinate_space,
                normalize=True,
            ),
            "injection_depth": _scalar_payload(
                injection_depth,
                unit=injection_depth_unit,
            ),
            "injection_angle": _scalar_payload(
                injection_angle,
                unit=injection_angle_unit,
            ),
        },
        "tissue": {
            # This should be a compact scalar in [0, 1], not a full tissue field.
            # Larger data should be referenced by path, not embedded in OSC.
            "tension_normalized": _scalar_payload(
                _clamp01_or_none(tension_normalized),
                unit="normalized",
            ),
            "warning_state": safe_warning_state,
            # These are references to larger data, if such data exists.  Empty
            # strings mean "not provided in this version/demo".
            "displacement_source": str(displacement_source or ""),
        },
        "semantic": {
            # For now this can point to a segmentation image, label volume, or
            # other Unity-loadable asset.  The sync layer does not interpret it.
            "labels_source": str(labels_source or ""),
        },
    }

    # Extra is a small escape hatch for experiments.  It should not become the
    # main contract.  If a field becomes important, promote it to the explicit
    # schema above so Unity and Python stay easy to read.
    if extra:
        state["extra"] = dict(extra)

    return state


def state_to_json(state: Mapping[str, Any]) -> str:
    """
    Convert a sync state dictionary into compact JSON for OSC transport.

    Compact JSON keeps UDP messages smaller.  The payload is still normal JSON,
    so it can be printed, logged, copied into a formatter, or parsed by Unity.
    """

    return json.dumps(state, separators=(",", ":"), ensure_ascii=True)


def build_state_json(**kwargs: Any) -> str:
    """
    Convenience helper for call sites that want JSON in one line.

    This simply combines:

        build_sync_state(...)
        state_to_json(...)
    """

    return state_to_json(build_sync_state(**kwargs))


def _scalar_payload(value: float | None, *, unit: str) -> dict[str, Any]:
    """
    Represent a scalar with an availability flag and a unit.

    Unity's JsonUtility does not have a great nullable-float story.  The
    explicit availability flag is clearer than relying on null/default behavior.
    """

    if value is None:
        return {"available": False, "value": 0.0, "unit": str(unit)}

    return {
        "available": True,
        "value": _safe_float(value, default=0.0),
        "unit": str(unit),
    }


def _vector_payload(
    value: Sequence[float] | Mapping[str, float] | None,
    *,
    default_z: float,
    space: str,
    normalize: bool = False,
) -> dict[str, Any]:
    """
    Represent a vector with an availability flag and coordinate space.

    Accepted input forms:

        (x, y)
        (x, y, z)
        {"x": x, "y": y}
        {"x": x, "y": y, "z": z}

    The len-2 form is useful for current OCT B-scan tracking, where the tracker
    returns a 2D image point.
    """

    parsed = _parse_vector(value, default_z=default_z)
    if parsed is None:
        return {
            "available": False,
            "x": 0.0,
            "y": 0.0,
            "z": 0.0,
            "space": str(space),
        }

    x, y, z = parsed

    if normalize:
        length = math.sqrt(x * x + y * y + z * z)
        if length > 0.0:
            x, y, z = x / length, y / length, z / length

    return {
        "available": True,
        "x": x,
        "y": y,
        "z": z,
        "space": str(space),
    }


def _parse_vector(
    value: Sequence[float] | Mapping[str, float] | None,
    *,
    default_z: float,
) -> tuple[float, float, float] | None:
    """Parse a user-provided vector into a safe (x, y, z) tuple."""

    if value is None:
        return None

    if isinstance(value, Mapping):
        if "x" not in value or "y" not in value:
            return None
        return (
            _safe_float(value.get("x"), default=0.0),
            _safe_float(value.get("y"), default=0.0),
            _safe_float(value.get("z", default_z), default=default_z),
        )

    if len(value) < 2:
        return None

    return (
        _safe_float(value[0], default=0.0),
        _safe_float(value[1], default=0.0),
        _safe_float(value[2], default=default_z) if len(value) >= 3 else float(default_z),
    )


def _safe_float(value: Any, *, default: float) -> float:
    """
    Convert a value to a finite float.

    JSON technically supports only normal numeric values.  NaN and Infinity tend
    to cause annoying cross-language behavior, so they are replaced by defaults.
    """

    try:
        result = float(value)
    except (TypeError, ValueError):
        return float(default)

    if not math.isfinite(result):
        return float(default)

    return result


def _clamp01_or_none(value: float | None) -> float | None:
    """Clamp an optional normalized value into the range Unity expects."""

    if value is None:
        return None

    safe = _safe_float(value, default=0.0)
    return max(0.0, min(1.0, safe))


def _normalize_warning_state(value: str | None) -> str:
    """Return a known warning-state string."""

    if value is None:
        return "unknown"

    normalized = str(value).strip().lower()
    if normalized not in ALLOWED_WARNING_STATES:
        return "unknown"

    return normalized
