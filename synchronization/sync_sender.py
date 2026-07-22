"""
Send synchronization state from Python to Unity over OSC.

The sender is deliberately small.  It does not know how sonification works and
it does not know how Unity visualizes anything.  Its only job is:

    1. build or accept a JSON-compatible sync payload
    2. send that payload to a Unity OSC receiver

The normal runtime endpoint is:

    /ioct/state_json

with one argument:

    json_payload: string

This module prefers the python-osc package when available, because the original
sonification environment already lists it as a dependency.  It also includes a
tiny fallback OSC encoder for "address + one string argument" so the sync bridge
can still run in a minimal Python environment.
"""

from __future__ import annotations

import socket
from typing import Any, Mapping

# Support both common ways of using this file:
#
#   1. Run from this folder:
#        python sync_sender.py
#
#   2. Import from the project root:
#        from synchronization.sync_sender import SyncSender
#
# The relative import works for package-style imports.  The fallback works when
# this file is executed directly as a script.
try:
    from .sync_state import build_sync_state, state_to_json
except ImportError:
    from sync_state import build_sync_state, state_to_json


DEFAULT_OSC_IP = "127.0.0.1"
DEFAULT_OSC_PORT = 12002


class SyncSender:
    """
    Small OSC sender used by the sonification frame loop.

    Typical usage:

        sync = SyncSender()

        for frame_index, frame in enumerate(frames):
            sync.send_state(
                frame_index=frame_index,
                timestamp_seconds=frame_index / 10.0,
                needle_tip=(x, y),
                injection_depth=depth,
                tension_normalized=tension,
                warning_state="none",
            )

        sync.send_end(final_frame_index=frame_index)

    Keep calls to this class near the place where the sonification code already
    knows the current frame index.  That makes it much easier to verify that
    Python and Unity are talking about the same OCT frame.
    """

    def __init__(
        self,
        *,
        ip: str = DEFAULT_OSC_IP,
        port: int = DEFAULT_OSC_PORT,
        enabled: bool = True,
        verbose: bool = False,
    ) -> None:
        self.ip = str(ip)
        self.port = int(port)
        self.enabled = bool(enabled)
        self.verbose = bool(verbose)

        # python-osc is nicer when available.  The fallback keeps this module
        # usable before the full sonification environment is installed.
        self._pythonosc_client = self._create_pythonosc_client()

        # The fallback sender uses a normal UDP socket.  It sends standard OSC
        # packets with a single string argument.
        self._socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    def send_state(self, **state_kwargs: Any) -> dict[str, Any]:
        """
        Build and send one frame state.

        The keyword arguments are passed directly to build_sync_state(...).
        Returning the state is convenient for debugging and tests:

            state = sync.send_state(...)
            print(state["frame_index"])
        """

        state = build_sync_state(**state_kwargs)
        self.send_json("/ioct/state_json", state)
        return state

    def send_capture_ready(
        self,
        *,
        capture_name: str,
        frame_count: int,
        frame_rate: float,
        extra: Mapping[str, Any] | None = None,
    ) -> dict[str, Any]:
        """
        Optionally tell Unity that a capture/sequence is about to start.

        This is useful if Unity wants to reset UI, allocate arrays, or display
        the current capture name before frame states arrive.  It is not required
        for the smallest possible sync demo; send_state(...) alone is enough to
        prove the frame-indexed path.
        """

        payload: dict[str, Any] = {
            "capture_name": str(capture_name),
            "frame_count": int(frame_count),
            "frame_rate": float(frame_rate),
        }

        if extra:
            payload["extra"] = dict(extra)

        self.send_json("/ioct/capture_ready", payload)
        return payload

    def send_end(
        self,
        *,
        capture_name: str = "",
        final_frame_index: int | None = None,
    ) -> dict[str, Any]:
        """
        Tell Unity that the synchronized stream has ended.

        Unity can use this to stop expecting new frames, freeze the final state,
        or write validation logs.
        """

        payload = {
            "capture_name": str(capture_name),
            "final_frame_index": None if final_frame_index is None else int(final_frame_index),
        }

        self.send_json("/ioct/end", payload)
        return payload

    def send_json(self, address: str, payload: Mapping[str, Any] | str) -> None:
        """
        Send a JSON payload to one OSC address.

        The payload can already be a JSON string, but normal code should pass a
        dictionary.  Keeping dictionaries until the last moment makes debugging
        easier.
        """

        if not self.enabled:
            return

        json_payload = payload if isinstance(payload, str) else state_to_json(payload)

        if self.verbose:
            print(f"[SyncSender] {address} -> {json_payload}")

        if self._pythonosc_client is not None:
            self._pythonosc_client.send_message(address, json_payload)
            return

        packet = _encode_osc_string_message(address, json_payload)
        self._socket.sendto(packet, (self.ip, self.port))

    def close(self) -> None:
        """Close the fallback UDP socket."""

        self._socket.close()

    def _create_pythonosc_client(self) -> Any | None:
        """
        Create a python-osc client if the package is installed.

        Importing inside the method avoids making python-osc a hard dependency
        for simple tests that only inspect JSON state generation.
        """

        try:
            from pythonosc.udp_client import SimpleUDPClient
        except ImportError:
            return None

        return SimpleUDPClient(self.ip, self.port)


def send_state_once(**kwargs: Any) -> dict[str, Any]:
    """
    Convenience helper for quick tests.

    Example:

        send_state_once(frame_index=0, needle_tip=(120, 80))

    For real playback, prefer creating one SyncSender and reusing it in the
    frame loop.  Reusing the sender avoids repeatedly creating sockets.
    """

    sender = SyncSender()
    try:
        return sender.send_state(**kwargs)
    finally:
        sender.close()


def _encode_osc_string_message(address: str, value: str) -> bytes:
    """
    Encode the tiny subset of OSC that this project needs.

    OSC strings are null-terminated and padded to a 4-byte boundary.
    A message with one string argument has this byte layout:

        padded address string
        padded type tag string ",s"
        padded argument string

    This is enough for Unity OSC receivers that expect a normal OSC address and
    one JSON string argument.
    """

    return (
        _osc_padded_string(address)
        + _osc_padded_string(",s")
        + _osc_padded_string(value)
    )


def _osc_padded_string(value: str) -> bytes:
    """
    Convert a Python string to an OSC padded string.

    OSC pads strings to a multiple of 4 bytes, including the trailing null byte.
    """

    raw = value.encode("utf-8") + b"\0"
    padding_length = (4 - (len(raw) % 4)) % 4
    return raw + (b"\0" * padding_length)


if __name__ == "__main__":
    # Manual smoke test:
    #
    #   python sync_sender.py
    #
    # Run Unity with the OSC receiver listening on port 12002, then execute this
    # file.  Unity should receive one /ioct/state_json message.
    sender = SyncSender(verbose=True)
    try:
        sender.send_state(
            frame_index=0,
            timestamp_seconds=0.0,
            capture_name="manual_test",
            needle_tip=(120.0, 80.0),
            needle_direction=(1.0, 0.0, 0.0),
            injection_depth=12.5,
            injection_angle=15.0,
            tension_normalized=0.25,
            warning_state="none",
        )
    finally:
        sender.close()
