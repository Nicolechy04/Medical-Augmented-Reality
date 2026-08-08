"""
Run the distance-based iOCT sonification pipeline.

For each frame in a scan, this module loads the required segmentation, tracks
the needle tip, estimates its position relative to the ILM and RPE, and maps
that information to audio parameters. It also manages optional video recording
and synchronization with the visualization application.

The audio communicates:
- distance to the next retinal boundary through pulse frequency,
- the current anatomical region through pitch and timbre, and
- local deformation through gain, harmonicity, and modulation.
"""

import argparse
import json
import os
import time
from time import sleep

import cv2
import numpy as np

from ioct_sonification_base import BaseIOCTSonification
from needle_tracker import NeedleTracker
from extrapolate import extrapolate_retina
from utils.sim_viz import (
    SonificationConfig,
    create_run_folder_and_save_data,
    move_recording_to_run,
)
from utils.util import handle_video_controls

# SuperCollider audio is optional at import time so the remaining pipeline can
# still start and report a clear warning when its audio dependencies are missing.
try:
    from distance_son import DistanceBasedSonification, SCRecorder

    AUDIO_AVAILABLE = True
except ImportError:
    print("⚠️  distance_son module not available.")
    AUDIO_AVAILABLE = False
    DistanceBasedSonification = None
    SCRecorder = None

from pathlib import Path
import sys

# Allow this sonification script to import the small sync bridge from:
# Full_Project/synchronization/
_SYNC_DIR = Path(__file__).resolve().parents[2] / "synchronization"
if str(_SYNC_DIR) not in sys.path:
    sys.path.append(str(_SYNC_DIR))

from sync_sender import SyncSender


sonification = None


def send_debug_message(message):
    """Forward a debug message to the shared sender, falling back to stdout."""
    try:
        from sonification_main import send_debug_message as debug_sender

        debug_sender(message)
    except Exception:
        print(f"Debug message: {message}")


def calculate_distance_to_next_layer(needle_tip_pos, ilm_line, rpe_line, anatomical_region):
    """Return the vertical pixel distance from the needle tip to the next layer.

    The target is the ILM while the tip is in the vitreous and the RPE while it
    is inside the retina. A tip already on either boundary has zero distance.
    ``inf`` indicates that no meaningful target can be determined.
    """
    if needle_tip_pos[0] is None or needle_tip_pos[1] is None:
        return float("inf"), "none"

    tip_x = int(np.clip(needle_tip_pos[0], 0, len(ilm_line) - 1))
    tip_y = needle_tip_pos[1]

    ilm_y = ilm_line[tip_x] if not np.isnan(ilm_line[tip_x]) else None
    rpe_y = rpe_line[tip_x] if not np.isnan(rpe_line[tip_x]) else None

    if anatomical_region == "vitreous" and ilm_y is not None:
        return max(0, ilm_y - tip_y), "ILM"
    if anatomical_region == "retina" and rpe_y is not None:
        return max(0, rpe_y - tip_y), "RPE"
    if anatomical_region in {"ILM", "RPE"}:
        return 0.0, anatomical_region

    return float("inf"), "none"


def map_distance_to_pulse_frequency(distance, max_distance=100.0, min_freq=0.5, max_freq=10.0, min_distance=20.0):
    """Map boundary distance in pixels to pulse rate in Hz.

    Approaching the target boundary increases the pulse rate. Distances at or
    beyond ``max_distance`` produce the minimum rate. The final audio-sending
    function clamps the resulting pulse rate before it reaches SuperCollider.
    """
    normalized_distance = (distance - min_distance) / (max_distance - min_distance)
    frequency = max_freq - (normalized_distance * (max_freq - min_freq))
    return max(min_freq, frequency)


def map_thickness_deformation_to_gain(deformation, min_gain=-20.0, max_gain=0.0):
    """Map normalized retinal compression to gain in dB using a square-root curve."""
    deformation = max(deformation, 0.0)
    return min_gain + (deformation ** 0.5) * (max_gain - min_gain)


def map_intensity_deformation_to_harmonicity(deformation, min_harm=1.0, max_harm=3.5):
    """Map normalized local intensity loss to the FM harmonicity ratio."""
    deformation = max(deformation, 0.0)  
    return min_harm + deformation * (max_harm - min_harm)


def map_intensity_deformation_to_mod_index(deformation, min_mod=0.0, max_mod=5.0):
    """Map normalized local intensity loss to FM modulation depth."""
    return min_mod + (deformation ** 2) * (max_mod - min_mod)


def map_region_to_timbre(anatomical_region, current_class, deformation=0.0):
    """Select the SuperCollider timbre preset for the current anatomy.

    Class 4 is the filled retinal region generated between the segmented ILM
    and RPE boundaries. Within that region, deformation continuously shifts the
    timbre rather than selecting only a fixed preset.
    """
    if anatomical_region == "vitreous" or current_class == 0:
        return 0.0
    if anatomical_region == "ILM" or current_class == 2:
        return 1.0
    if anatomical_region == "retina" or current_class == 4:
        return 2.0 + deformation * 1.0
    if anatomical_region == "RPE" or current_class == 3:
        return 3.0
    return 0.0


def compute_thickness_deformation(needle_tip_pos, current_ilm_line, current_rpe_line,
                                  baseline_thickness, context_window=40):
    """Estimate local retinal compression relative to the first frame.

    Thickness is sampled in a horizontal window around the tip. The median
    relative decrease is returned in [0, 1]; expansion and invalid measurements
    are treated as zero deformation.
    """
    if needle_tip_pos[0] is None:
        return 0.0

    tip_x = int(np.clip(needle_tip_pos[0], 0, len(current_ilm_line) - 1))
    x_start = max(0, tip_x - context_window)
    x_end = min(len(current_ilm_line), tip_x + context_window)

    current_thickness = current_rpe_line[x_start:x_end] - current_ilm_line[x_start:x_end]
    baseline_window = baseline_thickness[x_start:x_end]

    valid = (~np.isnan(current_thickness)) & (~np.isnan(baseline_window)) & (baseline_window > 5)
    if not np.any(valid):
        return 0.0

    relative_change = (baseline_window[valid] - current_thickness[valid]) / baseline_window[valid]
    return float(np.clip(np.nanmedian(relative_change), 0.0, 1.0))


def compute_intensity_deformation(frame, baseline_gray, needle_tip_pos, roi_size=80):
    """Estimate local darkening around the tip relative to the first frame.

    This secondary deformation cue compares mean grayscale intensity in a
    square ROI. Only intensity loss is represented; brightening is clipped to
    zero and the result is normalized to [0, 1].
    """
    if needle_tip_pos[0] is None:
        return 0.0

    if len(frame.shape) == 3:
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    else:
        gray = frame

    h, w = gray.shape
    tip_x, tip_y = int(needle_tip_pos[0]), int(needle_tip_pos[1])
    half = roi_size // 2

    y0 = max(0, tip_y - half)
    y1 = min(h, tip_y + half)
    x0 = max(0, tip_x - half)
    x1 = min(w, tip_x + half)

    if y1 <= y0 or x1 <= x0:
        return 0.0

    current_mean = float(np.mean(gray[y0:y1, x0:x1]))
    baseline_mean = float(np.mean(baseline_gray[y0:y1, x0:x1]))

    if baseline_mean < 5.0:
        return 0.0

    deformation = (baseline_mean - current_mean) / baseline_mean
    return float(np.clip(deformation, 0.0, 1.0))


def send_unified_sonification(pulse_frequency, base_tone_freq, thickness_def, intensity_def, timbre_value):
    """Convert the current cues to audio parameters and send one update.

    The SuperCollider connection is created lazily on the first valid update.
    Pitch and pulse rate are clamped here as a final safety boundary before the
    values leave Python.
    """
    global sonification

    if not AUDIO_AVAILABLE:
        return

    if sonification is None:
        try:
            sonification = DistanceBasedSonification()
        except Exception as exc:
            print(f"Could not start audio: {exc}")
            return

    clamped_freq = max(100.0, min(base_tone_freq, 4000.0))
    clamped_pulse = max(0.1, min(pulse_frequency, 20.0))

    gain = map_thickness_deformation_to_gain(thickness_def)
    harmonicity = map_intensity_deformation_to_harmonicity(intensity_def)
    mod_index = map_intensity_deformation_to_mod_index(intensity_def)

    try:
        sonification.update_params(clamped_freq, clamped_pulse, gain,
                                   harmonicity, mod_index, timbre_value)
    except Exception as exc:
        print(f"Error updating audio parameters: {exc}")


def stop_tone_generator():
    """Close the shared distance-sonification client and clear its global state."""
    global sonification

    if sonification is None:
        return

    try:
        sonification.close()
        sonification = None
        print("🔇 Audio tone generation stopped")
    except Exception as exc:
        print(f"⚠️  Error stopping audio: {exc}")


def get_needle_tip_pos_from_seg(needle_mask):
    """Approximate the needle tip as the lowest needle-labelled pixel.

    Image coordinates increase downward. The three-pixel vertical offset moves
    the reported point just beyond the segmented shaft endpoint.
    """
    if not np.any(needle_mask):
        return (None, None)

    ys, xs = np.where(needle_mask)
    if len(ys) == 0:
        return (None, None)

    max_y_idx = np.argmax(ys)
    tip_x, tip_y = xs[max_y_idx], ys[max_y_idx]
    return (float(tip_x), float(tip_y + 3))

# Should match the cv2.waitKey() delay in utils/util.py's handle_video_controls,
# which is the actual pacing of the main loop this value is labeling.
SYNC_FPS = 1.67


def valid_sync_tip(needle_tip_pos):
    """
    Return a clean needle tip for sync, or None if tracking failed.

    The tracker often uses (None, None) when it cannot find the needle.
    We send None in that case so Unity knows the tip is unavailable.
    """
    if needle_tip_pos is None:
        return None

    if len(needle_tip_pos) < 2:
        return None

    if needle_tip_pos[0] is None or needle_tip_pos[1] is None:
        return None

    return (float(needle_tip_pos[0]), float(needle_tip_pos[1]))


def safe_sync_send(sync_sender, **kwargs):
    """
    Send one sync message without letting network/sync issues crash sonification.

    Synchronization is important, but the sonification loop should still be able
    to close cleanly if Unity is not open or the receiver is not listening.
    """
    if sync_sender is None:
        return

    try:
        sync_sender.send_state(**kwargs)
    except Exception as exc:
        print(f"[sync] Could not send frame {kwargs.get('frame_index')}: {exc}")


class DistanceBasedIOCTSonifier(BaseIOCTSonification):
    """Distance-pipeline specialization of the shared iOCT session utilities."""

    def __init__(
        self,
        *,
        add_margin_to_roi=80,
        separate_ilm=True,
        ranges="INCREMENTAL",
        simulator_path="",
        target_resolution=(500, 500),
        max_needle_position_jump=100.0,
    ):
        super().__init__(
            num_nodes_x=5,
            num_nodes_y=40,
            extend_roi_to_needle_tip=False,
            add_margin_to_roi=add_margin_to_roi,
            sep_f_components=False,
            static_mapping_type="dClass",
            separate_ilm=separate_ilm,
            ranges=ranges,
            simulator_path=simulator_path,
            include_retina_in_ilm_rpe_drivers=False,
            thickness_statistic="median",
            use_confidence_weights=False,
            target_resolution=target_resolution,
            max_needle_position_jump=max_needle_position_jump,
        )

    def prepare_distance_initial_state(self, folder):
        """Load the first frame and establish all reference measurements.

        The initial segmentation provides the first ILM/RPE splines, needle-tip
        position, retinal-thickness baseline, and grayscale-intensity baseline.
        Segmentations are mandatory: no inference fallback is used.
        """
        initial_data = self.prepare_offline_initial_data(
            folder,
            self.add_margin_to_roi,
            fallback_loader=None,
        )

        if initial_data is None or initial_data.seg_img_0 is None:
            return None

        seg_img_0, ilm_line, rpe_line, baseline_thickness = self.prepare_initial_segmentation(
            initial_data.seg_img_0,
            rpe_thickness_pixels=0,
        )
        needle_tip_pos = get_needle_tip_pos_from_seg(seg_img_0 == 1)

        frame_0 = initial_data.frame_0
        if len(frame_0.shape) == 3:
            baseline_gray = cv2.cvtColor(frame_0, cv2.COLOR_BGR2GRAY)
        else:
            baseline_gray = frame_0.copy()

        return (initial_data, seg_img_0, [ilm_line.copy(), rpe_line.copy()],
                needle_tip_pos, baseline_thickness, baseline_gray)

    def load_distance_segmentation(self, frame_file, seg_file):
        """Load one required segmentation and reconstruct its retinal region.

        Existing class-4 pixels are cleared before ``extrapolate_retina`` fills
        the region between the current boundary labels. A missing or unreadable
        segmentation raises an error instead of invoking inference.
        """
        seg_img_current, _ = self.load_segmentation_from_source(
            seg_file,
            fallback_loader=None,
        )
        if seg_img_current is None:
            raise FileNotFoundError(
                f"Required segmentation could not be loaded for: {frame_file}"
            )

        seg_img_current = seg_img_current.copy()
        seg_img_current[seg_img_current == 4] = 0
        return extrapolate_retina(seg_img_current, cls_to_use=(4 if self.separate_ilm else 2))

    @staticmethod
    def detect_anatomical_region(needle_tip_pos, ilm_line, rpe_line, seg_img_current=None, tolerance=10):
        """Classify the tip relative to the ILM and RPE boundary splines.

        Boundary geometry is preferred over the pixel label because the tip may
        overlap the needle mask. The segmentation is used only as a fallback
        when one or both boundary values are unavailable. ``tolerance`` is the
        boundary band width in pixels.
        """
        current_class = 0
        anatomical_region = "background"
        boundary_info = {"ilm_y": None, "rpe_y": None}

        if needle_tip_pos[0] is None or needle_tip_pos[1] is None:
            return current_class, anatomical_region, boundary_info

        tip_x = int(np.clip(needle_tip_pos[0], 0, len(ilm_line) - 1))
        tip_y = int(needle_tip_pos[1])

        ilm_y = ilm_line[tip_x] if not np.isnan(ilm_line[tip_x]) else None
        rpe_y = rpe_line[tip_x] if not np.isnan(rpe_line[tip_x]) else None
        boundary_info["ilm_y"] = ilm_y
        boundary_info["rpe_y"] = rpe_y

        if ilm_y is not None and rpe_y is not None:
            if tip_y < ilm_y - tolerance:
                return 0, "vitreous", boundary_info
            if abs(tip_y - ilm_y) <= tolerance:
                return 2, "ILM", boundary_info
            if ilm_y + tolerance < tip_y < rpe_y - tolerance:
                return 4, "retina", boundary_info
            return 3, "RPE", boundary_info

        if seg_img_current is not None and 0 <= tip_x < seg_img_current.shape[1] and 0 <= tip_y < seg_img_current.shape[0]:
            seg_class = int(seg_img_current[tip_y, tip_x])
            if seg_class != 1:
                anatomical_region = {
                    0: "vitreous",
                    2: "ILM",
                    3: "RPE",
                    4: "retina",
                }.get(seg_class, "background")
                current_class = seg_class

        return current_class, anatomical_region, boundary_info

    @staticmethod
    def map_region_to_tone(anatomical_region, current_class):
        """Map anatomical regions to increasingly high base pitches in Hz."""
        if anatomical_region == "vitreous" or current_class == 0:
            return 200.0
        if anatomical_region == "ILM" or current_class == 2:
            return 800.0
        if anatomical_region == "retina" or current_class == 4:
            return 1500.0
        if anatomical_region == "RPE" or current_class == 3:
            return 3400.0
        return 200.0

    @staticmethod
    def save_synced_video(run_folder_path, video_frames_buffer):
        """Encode buffered display frames and preserve their timing metadata.

        The output frame rate is inferred from capture timestamps and clamped
        to a practical range. The JSON timestamps preserve the measured timing
        even though the MP4 itself uses a constant frame rate.
        """
        if len(video_frames_buffer) <= 1:
            print("⚠️ No video frames recorded or insufficient frames for video creation")
            return None

        print(f"🎬 Creating synced video with {len(video_frames_buffer)} frames...")
        total_time = video_frames_buffer[-1][1] - video_frames_buffer[0][1]
        if total_time > 0:
            avg_framerate = len(video_frames_buffer) / total_time
            avg_framerate = max(5.0, min(avg_framerate, 60.0))
        else:
            avg_framerate = 30.0

        height, width = video_frames_buffer[0][0].shape[:2]
        video_filename = os.path.join(run_folder_path, "simulation_video.mp4")
        fourcc = cv2.VideoWriter_fourcc(*"mp4v")
        video_writer = cv2.VideoWriter(video_filename, fourcc, avg_framerate, (width, height))

        for frame, _ in video_frames_buffer:
            video_writer.write(frame)

        video_writer.release()

        timing_file = os.path.join(run_folder_path, "video_timing.json")
        timing_data = {
            "total_frames": len(video_frames_buffer),
            "total_duration_seconds": total_time,
            "average_framerate": avg_framerate,
            "frame_timestamps": [timestamp for _, timestamp in video_frames_buffer],
            "video_filename": "simulation_video.mp4",
        }
        with open(timing_file, "w") as file_handle:
            json.dump(timing_data, file_handle, indent=2)

        print(f"📹 Synced video saved: {video_filename}")
        print(f"⏱️  Video duration: {total_time:.2f}s at {avg_framerate:.1f} FPS")
        print(f"📋 Timing data saved: {timing_file}")
        return video_filename


def parameterize_and_sonify_oct(
    folder,
    remap_with_segmentation=True,
    separate_ilm=True,
    refine_with_sam=None,
    ranges="INCREMENTAL",
    dynamic_segs=True,
    simulator_path="",
    save_video=True,
    target_resolution=(500, 500),
):
    """Process one scan folder and generate its distance-based sonification.

    The function initializes reference anatomy from the first frame, then runs
    the tracking, segmentation, sonification, synchronization, and visualization
    loop. On exit it closes all live resources and writes the run metadata,
    recorded audio, and optional video to a new output folder.

    Args:
        folder: Directory representing one scan. The shared base class resolves
            its OCT volume and matching segmentation directories.
        remap_with_segmentation: Periodically update ILM/RPE splines from the
            segmentation sequence.
        separate_ilm: Treat ILM and RPE as separate boundary classes when
            reconstructing the retinal region.
        refine_with_sam: Segmentation-refinement selection retained in the run
            configuration for compatibility with the shared pipeline.
        ranges: Named parameter-range configuration stored with the run.
        dynamic_segs: Use each frame's segmentation during needle tracking.
        simulator_path: Shared configuration field retained for compatibility;
            the distance audio path communicates directly with SuperCollider.
        save_video: Buffer the displayed frames and save an MP4 beside the audio.
        target_resolution: Optional ``(width, height)`` used by shared loading.

    Returns:
        Path to the generated run folder, or ``None`` when initialization fails.
    """
    refine_with_sam = [] if refine_with_sam is None else [int(item) for item in refine_with_sam]

    print(f"Visualizing folder: {folder}")
    if "_seg" in os.path.basename(folder):
        return

    session = DistanceBasedIOCTSonifier(
        add_margin_to_roi=80,
        separate_ilm=separate_ilm,
        ranges=ranges,
        simulator_path=simulator_path,
        target_resolution=target_resolution,
    )
    prepared = session.prepare_distance_initial_state(folder)
    if prepared is None:
        print(f"No frames or initial segmentation available for {folder}. Skipping...")
        return

    # The first frame defines fixed deformation baselines. Only the spline
    # estimates and tracked tip are updated as the scan advances.
    initial_data, seg_img_0, spline_prior_lines, needle_tip_pos, baseline_thickness, baseline_gray = prepared
    frame_files = initial_data.frame_files
    seg_files_path = initial_data.seg_files_path
    seg_files = initial_data.seg_files
    frame_0 = initial_data.frame_0

    if target_resolution is not None and (
        abs(initial_data.scale_x - 1.0) > 0.05 or abs(initial_data.scale_y - 1.0) > 0.05
    ):
        target_width, target_height = target_resolution
        print(
            f"🔄 Resizing from {initial_data.original_shape[1]}x{initial_data.original_shape[0]} "
            f"to {target_width}x{target_height}"
        )
        print(f"   Scale factors: x={initial_data.scale_x:.3f}, y={initial_data.scale_y:.3f}")

    print(f"Found {len(frame_files)} frames")
    print("Controls:")
    print("  Space: Pause/Resume")
    print("  B: Jump to beginning")
    print("  Left Arrow: Previous frame")
    print("  Right Arrow: Next frame")
    print("  G: Toggle Gaussian blur")
    print("  F: Toggle force display mode")
    print("  Escape: Exit")

    send_debug_message("Distance-based sonification initialized.")
    print("Starting Distance-Based Sonification")
    print("Beginning Distance-Based Sonification Loop")
    print("🔧 Initialized spline-based fitting:")
    print(f"   ILM line: {np.sum(~np.isnan(spline_prior_lines[0]))}/{len(spline_prior_lines[0])} valid points")
    print(f"   RPE line: {np.sum(~np.isnan(spline_prior_lines[1]))}/{len(spline_prior_lines[1])} valid points")

    tracker = NeedleTracker(
        init_frame=frame_0,
        tip_pos=needle_tip_pos,
        alpha=0.6,
        init_segmentation=seg_img_0 if "injection" not in folder.lower() else None,
    )
    print(f"Using {'segmentation' if dynamic_segs else 'template'}-based needle tracking")

    session.reset_tracking_state()
    # Spline fitting runs off the main thread because it is more expensive than
    # tracking and audio updates. The loop consumes recent results when ready.
    session.start_spline_worker(debug=False)
    print("🚀 Started background spline fitting thread")

    recorder = SCRecorder() if AUDIO_AVAILABLE and SCRecorder is not None else None
    if save_video:
        print("📹 Dynamic framerate video recording enabled")

    sonification_start_time = time.time()
    class_changes_log = []
    video_frames_buffer = []
    video_start_time = None
    audio_started = False
    paused = False
    show_force = False
    debug = False
    index = 0
    smoothed_thickness_def = 0.0
    smoothed_intensity_def = 0.0
    # Exponential smoothing suppresses frame-to-frame audio jitter while still
    # allowing the deformation cues to respond to sustained changes.
    DEFORMATION_ALPHA = 0.3

    # Python is the synchronization clock.  Unity should follow the frame_index
    # sent by this object instead of advancing its own timeline.
    sync_sender = SyncSender(ip="127.0.0.1", port=12002)
    capture_name = os.path.basename(folder)

    try:
        while index < len(frame_files):
            frame_file, frame = session.load_frame_at_index(folder, frame_files, index)
            if frame is None:
                print(f"Could not load frame: {frame_file}")
                index += 1
                continue

            seg_img_current = None
            if remap_with_segmentation or dynamic_segs:
                # Frame and segmentation lists are aligned by index. The loader
                # raises an error if the required segmentation is unavailable.
                seg_file = os.path.join(seg_files_path, seg_files[index]) if index < len(seg_files) else None
                seg_img_current = session.load_distance_segmentation(frame_file, seg_file)

            tracked_tip, _ = tracker.update(frame, segmentation_map=seg_img_current)
            needle_tip_pos = tracked_tip if tracked_tip is not None else (None, None)

            current_ilm_line = spline_prior_lines[0]
            current_rpe_line = spline_prior_lines[1]
            current_class, anatomical_region, boundary_info = session.detect_anatomical_region(
                needle_tip_pos,
                current_ilm_line,
                current_rpe_line,
                seg_img_current=seg_img_current,
            )

            if remap_with_segmentation and index % 5 == 0 and seg_img_current is not None:
                # Updating every fifth frame reduces fitting cost. The most
                # recent valid splines remain the prior between updates.
                session.queue_spline_task(index, seg_img_current)

            # Ignore results that are too old for the current anatomy; accepted
            # results become both the display segmentation and next spline prior.
            for result in session.collect_recent_spline_results(index, max_age=10):
                seg_img_current = result["fitted_seg"]
                spline_prior_lines = [
                    result["updated_lines"]["ILM"].copy(),
                    result["updated_lines"]["RPE"].copy(),
                ]

            current_ilm_line = spline_prior_lines[0]
            current_rpe_line = spline_prior_lines[1]

            distance_to_next_layer, target_layer = calculate_distance_to_next_layer(
                needle_tip_pos,
                current_ilm_line,
                current_rpe_line,
                anatomical_region,
            )
            # min/max_distance calibrated to this dataset's actual tracked range (~30-44px,
            # confirmed by debug logging) rather than the generic 20-100px default — the real
            # signal barely moves in absolute pixels, so it needs the full frequency range
            # mapped onto its actual span to be audible at all.
            pulse_frequency = map_distance_to_pulse_frequency(
                distance_to_next_layer,
                min_distance=28.0,
                max_distance=45.0,
                min_freq=1.0,
                max_freq=10.0,
            )
            base_tone_freq = session.map_region_to_tone(anatomical_region, current_class)

            thickness_def = compute_thickness_deformation(
                needle_tip_pos, current_ilm_line, current_rpe_line, baseline_thickness,
            )
            intensity_def = compute_intensity_deformation(
                frame, baseline_gray, needle_tip_pos,
            )
            smoothed_thickness_def = DEFORMATION_ALPHA * thickness_def + (1 - DEFORMATION_ALPHA) * smoothed_thickness_def
            smoothed_intensity_def = DEFORMATION_ALPHA * intensity_def + (1 - DEFORMATION_ALPHA) * smoothed_intensity_def
            timbre_value = map_region_to_timbre(anatomical_region, current_class, smoothed_thickness_def)

            send_unified_sonification(
                pulse_frequency, base_tone_freq,
                smoothed_thickness_def, smoothed_intensity_def, timbre_value,
            )

            # Send the same frame index that the sonification loop is using.
            # This is the important synchronization signal: Unity can display
            # exactly this frame instead of running an independent timer.
            sync_depth = distance_to_next_layer if np.isfinite(distance_to_next_layer) else None
            safe_sync_send(
                sync_sender,
                frame_index=index,
                timestamp_seconds=index / SYNC_FPS,
                capture_name=capture_name,
                needle_tip=valid_sync_tip(needle_tip_pos),
                injection_depth=sync_depth,
                injection_depth_unit="voxel",
                tension_normalized=None,
                warning_state="warning" if sync_depth is not None and sync_depth < 20 else "none",
            )

            if debug:
                info_y = 30
                cv2.putText(
                    frame,
                    f"Region: {anatomical_region}",
                    (10, info_y),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.6,
                    (0, 255, 255),
                    2,
                )
                cv2.putText(
                    frame,
                    f"Distance to {target_layer}: {distance_to_next_layer:.1f}px",
                    (10, info_y + 25),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.6,
                    (0, 255, 255),
                    2,
                )
                cv2.putText(
                    frame,
                    f"Pulse Frequency: {pulse_frequency:.1f}Hz",
                    (10, info_y + 50),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.6,
                    (0, 255, 255),
                    2,
                )
                cv2.putText(
                    frame,
                    f"Base Tone: {base_tone_freq:.0f}Hz",
                    (10, info_y + 75),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.6,
                    (0, 255, 255),
                    2,
                )
                if boundary_info["ilm_y"] is not None and boundary_info["rpe_y"] is not None:
                    cv2.putText(
                        frame,
                        f"ILM: {boundary_info['ilm_y']:.0f}  RPE: {boundary_info['rpe_y']:.0f}",
                        (10, info_y + 100),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        0.5,
                        (0, 255, 0),
                        1,
                    )

            cv2.imshow("Video Visualization Tool", frame)

            if save_video:
                # Audio recording begins with the first buffered video frame so
                # both outputs share the same practical start point.
                start_callback = recorder.start if recorder is not None else (lambda: None)
                video_start_time, audio_started = session.buffer_video_frame(
                    video_frames_buffer,
                    frame,
                    video_start_time,
                    audio_started=audio_started,
                    start_audio_callback=start_callback,
                )
            elif not audio_started and recorder is not None:
                recorder.start()
                audio_started = True

            control_result = handle_video_controls(paused, index, len(frame_files), show_force)
            if control_result["exit"]:
                break

            paused = control_result["paused"]
            index = control_result["index"]
            show_force = control_result["show_force"]
    finally:
        # This block owns all live resources so early exit and runtime errors do
        # not leave windows, worker threads, audio, or sync sockets open.
        cv2.destroyAllWindows()
        session.stop_spline_worker(timeout=2.0)

        if sonification is not None:
            try:
                sonification.reset()
            except Exception:
                pass

        if audio_started and recorder is not None:
            recorder.stop()

        # Mark the stream as complete and release the UDP socket.  This stays in
        # the cleanup block so Unity is notified even when the user exits early.
        try:
            sync_sender.send_end(
                capture_name=capture_name,
                final_frame_index=index,
            )
        finally:
            sync_sender.close()

        stop_tone_generator()

    # Runtime outputs are written only after live processing has shut down. The
    # configuration snapshot makes each recording inspectable and reproducible.
    config = SonificationConfig(
        script_type=os.path.basename(folder),
        remap_with_segmentation=remap_with_segmentation,
        separate_ilm=separate_ilm,
        refine_with_sam=refine_with_sam,
        ranges=ranges,
        simulator_path=simulator_path,
        has_needle=True,
        is_synthetic=False,
        angle=0,
        needle_tip_pos=needle_tip_pos,
        masses=None,
        stiffnesses=None,
        damping=None,
        seg_img_0=seg_img_0,
    )
    run_folder_path = create_run_folder_and_save_data(
        folder="distance_based",
        sonification_start_time=sonification_start_time,
        class_changes_log=class_changes_log,
        sound_model_config=None,
        config=config,
        sample_name=os.path.basename(folder),
        model_type="distance_based",
    )

    if save_video:
        session.save_synced_video(run_folder_path, video_frames_buffer)

    # Give SuperCollider time to finish writing the temporary WAV before moving
    # it into the newly created run folder.
    sleep(2)

    move_recording_to_run(
        "/tmp/rec_son.wav",
        run_folder_path,
    )

    return run_folder_path


if __name__ == "__main__":
    # Single mode processes one scan. Batch mode discovers subjects whose
    # ``iOCT Microscope/Volume`` directory matches the expected dataset layout.
    parser = argparse.ArgumentParser(description="Distance-based Sonification Tool")
    parser.add_argument("--folder_mode", default="single", help="Mode: 'single' for single folder, 'batch' for batch processing")
    parser.add_argument("root_folder", help="Path to the root folder containing video frame subfolders")
    parser.add_argument("--refine_with_sam", nargs="+", help="Masks to refine")
    parser.add_argument("--ranges", default="INCREMENTAL", help="Parameter ranges configuration")
    parser.add_argument(
        "--dynamic_segs",
        action="store_true",
        default=True,
        help="Use segmentation-based needle tracking instead of template matching",
    )
    parser.add_argument(
        "--simulator_path",
        default="",
    )
    parser.add_argument(
        "--save_video",
        action="store_true",
        default=True,
        help="Save the visualization as an MP4 video file",
    )
    args = parser.parse_args()

    if args.folder_mode == "single":
        parameterize_and_sonify_oct(
            args.root_folder,
            refine_with_sam=args.refine_with_sam or [],
            ranges=args.ranges,
            dynamic_segs=args.dynamic_segs,
            simulator_path=args.simulator_path,
            save_video=args.save_video,
        )
    else:
        import glob

        folders = sorted(
            folder_path
            for folder_path in glob.glob(os.path.join(args.root_folder, "*"))
            if os.path.isdir(
                os.path.join(folder_path, "iOCT Microscope", "Volume")
            )
        )
        for folder_path in folders:
            if os.path.isdir(folder_path):
                parameterize_and_sonify_oct(
                    folder_path,
                    refine_with_sam=args.refine_with_sam or [],
                    ranges=args.ranges,
                    dynamic_segs=args.dynamic_segs,
                    simulator_path=args.simulator_path,
                    save_video=args.save_video,
                )
