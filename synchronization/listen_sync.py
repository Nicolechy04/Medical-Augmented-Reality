"""
Listen for synchronization messages from the sonification scripts.

This is a small test tool. It lets you check whether Python is really sending
its frame clock without opening Unity.

Run it in one terminal:

    python synchronization/listen_sync.py

Then run a sonification demo in another terminal. If synchronization is working,
this listener prints one line per received frame.
"""

from __future__ import annotations

import argparse
import json
import socket


def main() -> None:
    parser = argparse.ArgumentParser(description="Listen for iOCT synchronization OSC messages.")
    parser.add_argument("--ip", default="127.0.0.1", help="IP address to bind.")
    parser.add_argument("--port", type=int, default=12002, help="UDP/OSC port to listen on.")
    parser.add_argument("--raw", action="store_true", help="Print full raw JSON payloads.")
    args = parser.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind((args.ip, args.port))

    print(f"Listening for sync messages on {args.ip}:{args.port}")
    print("Start the sonification demo in another terminal. Press Ctrl+C to stop.\n")

    try:
        while True:
            data, sender = sock.recvfrom(65535)
            decoded = decode_osc_string_message(data)
            if decoded is None:
                print(f"Ignored non-sync packet from {sender}")
                continue

            address, json_payload = decoded

            if args.raw:
                print(address, json_payload)
                continue

            if address == "/ioct/state_json":
                print_state(json_payload)
            elif address == "/ioct/end":
                print("END", json_payload)
            else:
                print(address, json_payload)
    except KeyboardInterrupt:
        print("\nStopped listener.")
    finally:
        sock.close()


def print_state(json_payload: str) -> None:
    """Print only the fields needed to confirm frame-clock synchronization."""

    try:
        state = json.loads(json_payload)
    except json.JSONDecodeError:
        print("Could not parse JSON:", json_payload[:300])
        return

    frame_index = state.get("frame_index")
    timestamp = state.get("timestamp_seconds")
    capture_name = state.get("capture_name", "")

    needle = state.get("needle", {})
    tip = needle.get("tip_position", {})
    tip_available = tip.get("available", False)

    if tip_available:
        tip_text = f"tip=({tip.get('x'):.1f}, {tip.get('y'):.1f}, {tip.get('z'):.1f})"
    else:
        tip_text = "tip=unavailable"

    print(
        f"frame={frame_index:>4}  "
        f"time={timestamp:>7.3f}s  "
        f"capture={capture_name}  "
        f"{tip_text}"
    )


def decode_osc_string_message(data: bytes) -> tuple[str, str] | None:
    """
    Decode the simple OSC packet sent by synchronization/sync_sender.py.

    The expected packet shape is:

        address string
        type tag string ",s"
        one JSON string argument
    """

    offset = 0
    address, offset = read_osc_string(data, offset)
    if address is None:
        return None

    type_tag, offset = read_osc_string(data, offset)
    if type_tag != ",s":
        return None

    value, offset = read_osc_string(data, offset)
    if value is None:
        return None

    return address, value


def read_osc_string(data: bytes, offset: int) -> tuple[str | None, int]:
    """Read one null-terminated, 4-byte-padded OSC string."""

    if offset < 0 or offset >= len(data):
        return None, offset

    start = offset
    end = start
    while end < len(data) and data[end] != 0:
        end += 1

    if end >= len(data):
        return None, offset

    value = data[start:end].decode("utf-8", errors="replace")

    offset = end + 1
    while offset % 4 != 0:
        offset += 1

    return value, offset


if __name__ == "__main__":
    main()
