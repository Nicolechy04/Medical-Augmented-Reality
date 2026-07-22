"""Minimal OSC clients for controlling and recording the SuperCollider synth."""

from pythonosc.udp_client import SimpleUDPClient


class DistanceBasedSonification:
    """Send real-time distance-pipeline parameters to SuperCollider."""
    def __init__(self, ip="127.0.0.1", port=57120):
        self.osc = SimpleUDPClient(ip, port)

    def update_params(self, freq, pulse_freq, gain=-20,
                      harmonicity=1.0, mod_index=0.0, timbre=0.0):
        """Send parameters in the order expected by ``/son/update`` in the SCD file."""
        self.osc.send_message("/son/update", [
            float(freq),
            float(pulse_freq),
            float(gain),
            float(harmonicity),
            float(mod_index),
            float(timbre),
        ])

    def close(self):
        pass

    def reset(self):
        """Silence the synth while restoring neutral modulation parameters."""
        self.osc.send_message("/son/update", [400, 0.5, -120, 1.0, 0.0, 0.0])


class SCRecorder:
    """Control SuperCollider's temporary WAV recording over OSC."""
    def __init__(self, ip="127.0.0.1", port=57120):
        self.client = SimpleUDPClient(ip, port)

    def start(self):
        self.client.send_message("/rec/start", [])

    def stop(self):
        self.client.send_message("/rec/stop", [])
