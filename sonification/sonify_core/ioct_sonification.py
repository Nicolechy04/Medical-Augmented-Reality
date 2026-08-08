"""
Run the physical-model iOCT sonification pipeline.

Each scan frame is combined with its required segmentation to track the needle,
update a node grid representing retinal anatomy, and derive force/deformation
cues. Those cues drive a Processing-based physical sound model while separate
audio timers send A-scan and ILM/RPE updates at stable rates independent of the
video-processing speed.

The module also manages the live OpenCV preview, optional synchronized video
recording, run metadata, and frame synchronization with the visualization app.
"""

import time
from time import sleep
import cv2
import os
import argparse
import numpy as np
import threading
from ioct_sonification_base import BaseIOCTSonification
from utils.util import get_force_sum_for_frame, load_force_data, handle_video_controls
from sonification_main import set_sonification_params, sonify_ILM_RPE, sonify_ascan, send_debug_message, start_recording, stop_recording, set_crackle_params
from needle_tracker import NeedleTracker
from utils.sim_viz import *
from utils.util import *
from forces import compute_node_magnitudes
from extrapolate import extract_line, RPE_LABEL, ILM_LABEL
from utils.processing_utils import kill_process_processing
from sonification_main import transmitter
from pathlib import Path
import sys

# Allow this sonification script to import the small sync bridge from:
# Full_Project/synchronization/
_SYNC_DIR = Path(__file__).resolve().parents[2] / "synchronization"
if str(_SYNC_DIR) not in sys.path:
    sys.path.append(str(_SYNC_DIR))

from sync_sender import SyncSender

# Should match the cv2.waitKey() delay in utils/util.py's handle_video_controls,
# which is the actual pacing of the main loop this value is labeling.
SYNC_FPS = 1.67

# Distinct crackle-texture cue per anatomical class (see extrapolate.py for
# ILM_LABEL=2/RPE_LABEL=3; 0=background/vitreous, 1=needle, 4=retina interior),
# triggered once whenever the needle tip crosses into a new region so each
# anatomical layer has a recognizable acoustic signature, independent of the
# per-node mass/stiffness/damping profile that already shapes the A-scan model.
REGION_CRACKLE_CUES = {
    0: (0.0, 0.0),   # background / vitreous - silent
    2: (2.5, 0.6),   # ILM boundary - light crackle
    3: (5.0, 1.2),   # RPE boundary - dense, louder crackle
    4: (1.0, 0.3),   # retina interior - subtle texture
}


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

class VariableRateIOCTSonifier(BaseIOCTSonification):
    """Shared iOCT session with independently timed physical-audio streams.

    Frame processing produces the newest geometry and force values whenever it
    can. Two background timers consume that shared state: one updates the node
    A-scan at ``sonification_rate`` and the other updates the slower ILM/RPE cue
    at ``ilm_rpe_rate``. A lock prevents either timer from reading partial data.
    """

    def __init__(self, *, sonification_rate, ilm_rpe_rate, **kwargs):
        super().__init__(**kwargs)
        self.sonification_rate = sonification_rate
        self.ilm_rpe_rate = ilm_rpe_rate
        # This dictionary is the hand-off point between the frame-processing
        # thread and both audio timer threads. Mutable values are accessed under
        # ``lock``; events provide cooperative shutdown for the workers.
        self.sonification_state = {
            'lock': threading.Lock(),
            'latest_node_magnitudes': None,
            'latest_current_class': 0,
            'latest_f_ILM': 0.0,
            'latest_f_RPE': 0.0,
            'smoothed_f_ILM': 0.0,
            'smoothed_f_RPE': 0.0,
            'sonification_ready': False,
            'sep_f_components': False,
            'timer': None,
            'stop_event': threading.Event(),
            'sonification_rate': sonification_rate,
            'ilm_rpe_rate': ilm_rpe_rate,
            'ilm_rpe_timer': None,
            'ilm_rpe_stop_event': threading.Event(),
            'latest_needle_pos': (None, None),
            'roi_bounds': None,
            'latest_dist_to_anatomy': float('inf'),
            'f_ILM_is_active': False,
            'f_ILM_ramp_counter': 0,
            'f_ILM_ramp_duration': 1.0,
        }

    def configure_sonification_state(self):
        """Reset timer-visible values before processing a new scan."""
        state = self.sonification_state
        with state['lock']:
            state['latest_node_magnitudes'] = None
            state['latest_current_class'] = 0
            state['latest_f_ILM'] = 0.0
            state['latest_f_RPE'] = 0.0
            state['smoothed_f_ILM'] = 0.0
            state['smoothed_f_RPE'] = 0.0
            state['sonification_ready'] = False
            state['sep_f_components'] = self.sep_f_components
            state['sonification_rate'] = self.sonification_rate
            state['ilm_rpe_rate'] = self.ilm_rpe_rate
            state['latest_needle_pos'] = (None, None)
            state['roi_bounds'] = None
            state['latest_dist_to_anatomy'] = float('inf')
            state['f_ILM_is_active'] = False
            state['f_ILM_ramp_counter'] = 0
        state['timer'] = None
        state['ilm_rpe_timer'] = None
        state['stop_event'].clear()
        state['ilm_rpe_stop_event'].clear()
        return state

    def calculate_f_ilm_ramp_intensity(self):
        """Return the current fade-in multiplier for the ILM deformation cue."""
        with self.sonification_state['lock']:
            return self.calculate_ramp_intensity(
                is_active=self.sonification_state['f_ILM_is_active'],
                ramp_counter=self.sonification_state['f_ILM_ramp_counter'],
                sonification_rate=self.sonification_state['sonification_rate'],
                ramp_duration=self.sonification_state['f_ILM_ramp_duration'],
            )

    def update_f_ilm_ramp_state(self, f_ilm_active):
        """Start, continue, or reset the ILM cue's click-preventing fade-in."""
        with self.sonification_state['lock']:
            is_active, ramp_counter = self.update_ramp_state(
                self.sonification_state['f_ILM_is_active'],
                self.sonification_state['f_ILM_ramp_counter'],
                f_ilm_active,
            )
            started = f_ilm_active and not self.sonification_state['f_ILM_is_active']
            stopped = (not f_ilm_active) and self.sonification_state['f_ILM_is_active']
            self.sonification_state['f_ILM_is_active'] = is_active
            self.sonification_state['f_ILM_ramp_counter'] = ramp_counter

        if started:
            print("🔊 f_ILM ramping started - beginning soft")
        elif stopped:
            print("🔇 f_ILM ramping stopped - resetting")

    def sonification_timer_callback(self):
        """Send the newest node-force vector to the A-scan sound model."""
        try:
            if not self.sonification_state['sonification_ready']:
                return

            with self.sonification_state['lock']:
                node_magnitudes = self.sonification_state['latest_node_magnitudes'].copy() if self.sonification_state['latest_node_magnitudes'] is not None else None
                sep_f_components = self.sonification_state['sep_f_components']

            if node_magnitudes is None:
                return

            sonify_ascan(forces=node_magnitudes, separate_f_components=sep_f_components)
        except Exception as e:
            print(f'❌ Sonification callback error: {str(e)}')

    def ilm_rpe_timer_callback(self):
        """Send the slower boundary-deformation cue when anatomy is relevant.

        The cue is active only when the tip is assigned to anatomy and is either
        inside the model ROI or close to its nearest node. This prevents remote
        or background tracking noise from producing a tissue-deformation sound.
        """
        try:
            if not self.sonification_state['sonification_ready']:
                return

            with self.sonification_state['lock']:
                current_class = self.sonification_state['latest_current_class']
                f_ILM = self.sonification_state['smoothed_f_ILM']
                f_RPE = self.sonification_state['smoothed_f_RPE']
                needle_pos = self.sonification_state['latest_needle_pos']
                roi_bounds = self.sonification_state['roi_bounds']
                dist_to_anatomy = self.sonification_state['latest_dist_to_anatomy']

            needle_in_roi = False
            if needle_pos[0] is not None and needle_pos[1] is not None and roi_bounds is not None:
                x0, y0, side_x, side_y = roi_bounds
                needle_in_roi = (x0 <= needle_pos[0] <= x0 + side_x and y0 <= needle_pos[1] <= y0 + side_y)

            needle_close_to_anatomy = dist_to_anatomy < 50.0

            if current_class != 0 and (needle_in_roi or needle_close_to_anatomy):
                f_ILM_should_be_active = abs(f_ILM + f_RPE) > 1
                self.update_f_ilm_ramp_state(f_ILM_should_be_active)

                if f_ILM_should_be_active:
                    ramp_intensity = self.calculate_f_ilm_ramp_intensity()
                    ramped_f_ILM = f_ILM * ramp_intensity
                    sonify_ILM_RPE(f_ilm=ramped_f_ILM * 3 / 5, f_rpe=0)
            else:
                self.update_f_ilm_ramp_state(False)
        except Exception as e:
            print(f'❌ ILM/RPE sonification callback error: {str(e)}')

    def start_sonification_timer(self):
        """Start daemon workers for the independent A-scan and boundary rates."""
        def timer_worker():
            timer_period = 1.0 / self.sonification_state['sonification_rate']
            print(f'🎵 Sonification timer started at {self.sonification_state["sonification_rate"]}Hz ({timer_period*1000:.1f}ms period)')

            while not self.sonification_state['stop_event'].is_set():
                self.sonification_timer_callback()
                time.sleep(timer_period)

        timer_thread = threading.Thread(target=timer_worker, daemon=True)
        timer_thread.start()
        self.sonification_state['timer'] = timer_thread

        def ilm_rpe_worker():
            ilm_rpe_period = 1.0 / self.sonification_state['ilm_rpe_rate']
            print(f'🎵 ILM/RPE timer started at {self.sonification_state["ilm_rpe_rate"]}Hz ({ilm_rpe_period*1000:.1f}ms period)')
            while not self.sonification_state['ilm_rpe_stop_event'].is_set():
                self.ilm_rpe_timer_callback()
                time.sleep(ilm_rpe_period)

        ilm_rpe_thread = threading.Thread(target=ilm_rpe_worker, daemon=True)
        ilm_rpe_thread.start()
        self.sonification_state['ilm_rpe_timer'] = ilm_rpe_thread
        return timer_thread

    def stop_sonification_timer(self):
        """Signal both audio workers and wait briefly for clean termination."""
        self.sonification_state['stop_event'].set()
        self.sonification_state['ilm_rpe_stop_event'].set()
        if self.sonification_state['timer']:
            self.sonification_state['timer'].join(timeout=1.0)
        if self.sonification_state['ilm_rpe_timer']:
            self.sonification_state['ilm_rpe_timer'].join(timeout=1.0)
        print('🎵 Sonification timers stopped')


def parameterize_and_sonify_oct(
        folder, 
        num_nodes_x=5, 
        num_nodes_y=60, 
        extend_roi_to_needle_tip=False, 
        add_margin_to_roi=80, 
        sep_f_components=False, 
        remap_with_segmentation=True,
        use_handle_forces=False,
        static_mapping_type="dClass",
        dynamic_detuning=False,
        use_deflection_scaling=True,
        deflection_debug=False,
        use_dynamic_intensity_mapping=True,
        separate_ilm=True,
        refine_with_sam=[],
        ranges="INCREMENTAL",
        dynamic_segs=True,
        simulator_path="",
        save_video=True,
        use_confidence_weights=True,
        include_retina_in_ilm_rpe_drivers=True,
        thickness_statistic="median",
        target_resolution=(512, 512),  # (width, height) - normalize to injection resolution
        sonification_rate=60.0,  # Hz - A-scan sonification frequency
        ilm_rpe_rate=10.0,  # Hz - ILM/RPE boundary sonification frequency
        use_huber_regressor=True,  # Use Huber regressor instead of polyfit for robust fitting
        max_needle_position_jump=100.0,  # Maximum allowed needle position jump in pixels to prevent artifacts
    ):
    """Process one scan with the node-based physical sonification model.

    Initialization normalizes the OCT data, aligns the retina horizontally,
    constructs and classifies the physical-model node grid, sends its initial
    parameters to the Processing simulator, and starts fixed-rate audio workers.
    The frame loop then tracks the needle, updates retinal splines and node
    positions, estimates local deformation, and publishes the latest force state.

    The most important configuration groups are:
    - ``num_nodes_x``/``num_nodes_y``: resolution of the physical node grid;
    - ROI and segmentation options: geometry used to initialize/update the grid;
    - mapping options: conversion from image classes to physical parameters;
    - ``sonification_rate``/``ilm_rpe_rate``: independent audio update rates;
    - confidence and fitting options: robustness to uncertain segmentations.

    Segmentations are mandatory in this cleaned pipeline; inference is not used
    as a fallback. On success, the function returns the generated run directory.
    """
    print(f"Visualizing folder: {folder}")
    deforming = False

    if "injection" not in folder.lower():
        # Non-injection sequences use a larger ROI that explicitly reaches the
        # needle tip because their framing differs from injection recordings.
        extend_roi_to_needle_tip = True
        add_margin_to_roi = 120

    assert static_mapping_type in ["intensity", "dRPE", "dClass"], "static_mapping_type must be 'intensity', 'dRPE' or 'dClass'"
    session = VariableRateIOCTSonifier(
        sonification_rate=sonification_rate,
        ilm_rpe_rate=ilm_rpe_rate,
        num_nodes_x=num_nodes_x,
        num_nodes_y=num_nodes_y,
        extend_roi_to_needle_tip=extend_roi_to_needle_tip,
        add_margin_to_roi=add_margin_to_roi,
        sep_f_components=sep_f_components,
        static_mapping_type=static_mapping_type,
        separate_ilm=separate_ilm,
        ranges=ranges,
        simulator_path=simulator_path,
        include_retina_in_ilm_rpe_drivers=include_retina_in_ilm_rpe_drivers,
        thickness_statistic=thickness_statistic,
        use_confidence_weights=use_confidence_weights,
        target_resolution=target_resolution,
        max_needle_position_jump=max_needle_position_jump,
    )
    sonification_state = session.configure_sonification_state()
    print(f"🎵 Sonification rate set to {sonification_rate}Hz, ILM/RPE rate set to {ilm_rpe_rate}Hz")

    # Recorded force data is used when present. Later code can derive a motion
    # proxy from needle-tip speed when no matching force measurement exists.
    force_data = load_force_data(folder)

    # Get label.json
    label_file = os.path.join(folder, "label.json")
    labels = {}

    # Return if folder name contains _seg already
    if "_seg" in os.path.basename(folder):
        return

    initial_data = session.prepare_offline_initial_data(
        folder,
        add_margin_to_roi,
        fallback_loader=None,
    )
    if initial_data is None:
        print(f"No frames found in folder {folder}. Skipping...")
        return

    frame_files = initial_data.frame_files
    seg_files_path = initial_data.seg_files_path
    seg_files = initial_data.seg_files

    # Other control variables
    paused = False
    label = ""
    show_force = True
    prev_class = None
    debug = False
 
    # Control information
    print(f"Found {len(frame_files)} frames")
    print("Controls:")
    print("  Space: Pause/Resume")
    print("  B: Jump to beginning")
    print("  Left Arrow: Previous frame")
    print("  Right Arrow: Next frame")
    print("  G: Toggle Gaussian blur")
    print("  F: Toggle force display mode")
    print("  Escape: Exit")

    index = 0

    # The first frame establishes the coordinate transform, anatomical baselines,
    # ROI, model grid, and initial needle position used by later frames.
    frame_0 = initial_data.frame_0
    seg_img_0 = initial_data.seg_img_0
    if initial_data.seg_img_0 is None:
        raise FileNotFoundError(
            f"No initial segmentation found for: {folder}"
        )
    global_confidence = initial_data.initial_confidence
    scale_x = initial_data.scale_x
    scale_y = initial_data.scale_y
    add_margin_to_roi = initial_data.add_margin_to_roi
    if target_resolution is not None and (abs(scale_x - 1.0) > 0.05 or abs(scale_y - 1.0) > 0.05):
        target_width, target_height = target_resolution
        print(f"🔄 Resizing from {initial_data.original_shape[1]}x{initial_data.original_shape[0]} to {target_width}x{target_height}")
        print(f"   Scale factors: x={scale_x:.3f}, y={scale_y:.3f}")
        print(f"   Scaled add_margin_to_roi: {add_margin_to_roi}")

    geometry = session.prepare_initial_geometry(
        frame_0,
        seg_img_0,
        rpe_thickness_pixels=5 if "injection" in folder.lower() else 3,
        injection_mode="injection" in folder.lower(),
        use_huber_regressor=use_huber_regressor,
        margin=add_margin_to_roi,
        extend_tip=extend_roi_to_needle_tip,
    )
    seg_img_0 = geometry.seg_img_0
    ILM_0 = geometry.ilm_line
    RPE_0 = geometry.rpe_line
    frame_0_rotated = geometry.frame_0_rotated
    seg_0_rotated = geometry.seg_0_rotated
    needle_tip_pos = geometry.needle_tip_pos
    ROI = geometry.roi
    rotated_line_points = geometry.rotated_line_points
    M = session.M
    angle = session.angle

    # These values are maintained by the base class across successive spline and
    # thickness updates; copies prevent accidental mutation of its initial state.
    state = session.state
    target_thickness = session.target_thickness
    prev_thickness = session.prev_thickness.copy()
    prev_ILM_line = session.prev_ILM_line.copy()
    prev_RPE_line = session.prev_RPE_line.copy()

    # Deformation is compared in the rotated coordinate system, where the retinal
    # boundaries are approximately horizontal and vertical displacement is more
    # meaningful. Jitter is introduced only as an uncertainty cue.
    # === DEFORMATION TRACKING INITIALIZATION ===
    deformation_ref = 5
    JITTER_MAX_HZ = 4.0

    
    
    # seg_img_0[seg_img_0 == 2] = 4
    seg_colors = [(255,255,0), (0,255,255), (255,0,255), (255,255,255)]
    seg_img_colored = frame_0.copy()
    for i, label in enumerate(np.unique(seg_img_0)):
        if label == 0:
            continue  # skip background
        mask = (seg_img_0 == label).astype(np.uint8) * 255
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        color = seg_colors[i % len(seg_colors)]
        cv2.drawContours(seg_img_colored, contours, -1, color, 1)
    
    # Store ROI dimensions needed for dynamic segmentation
    side_x, side_y = None, None

    print("Initial Needle Position and ROI alignment")

    # Show frame_0 and seg_img_0 with cv2
    if debug:
        cv2.imshow("Initial Position 0", frame_0)
        cv2.imshow("Initial Position 0", seg_img_colored)
        cv2.waitKey(0)
        cv2.destroyAllWindows()

    seg_0_rotated_rgb = cv2.warpAffine(seg_img_colored, M, (frame_0.shape[1], frame_0.shape[0]))

    # Use the same rotation for all thickness comparisons. Measuring before
    # rotation would mix anatomical deformation with changes in retinal slope.
    # === INITIALIZE ROTATED FRAME THICKNESS TRACKING AFTER M IS DEFINED ===
    # Apply rotation to initial segmentation for accurate thickness measurements
    ILM_0_rotated = extract_line(seg_0_rotated, ILM_LABEL)
    RPE_0_rotated = extract_line(seg_0_rotated, RPE_LABEL)
    baseline_thickness_rotated = RPE_0_rotated - ILM_0_rotated
    prev_thickness_rotated = baseline_thickness_rotated.copy()
    prev_ILM_line_rotated = ILM_0_rotated.copy()
    prev_RPE_line_rotated = RPE_0_rotated.copy()

    if ROI is None:
        x0, y0, side_x, side_y = 0, 0, 1, 1
    else:
        x0, y0, side_x, side_y = ROI
        # Visualize ROI
        frame_with_roi = frame_0_rotated.copy()
        if debug:
            cv2.rectangle(frame_with_roi, (x0, y0), (x0 + side_x, y0 + side_y), (0, 0, 255), 2)
            cv2.polylines(frame_with_roi, [rotated_line_points], False, (0, 255, 0), 1)
            cv2.imshow("ROI Intersection", frame_with_roi)
            cv2.waitKey(0)
            cv2.destroyAllWindows()

    # Convert the aligned ROI into a classified node grid and map each node class
    # to its initial mass, stiffness, and damping parameters.
    
    model_setup = session.initialize_sound_model_setup(
        geometry,
        classification_mode="median",
        enforce_consistency=False,
        debug=debug,
    )
    frame_roi = model_setup.frame_roi
    seg_roi = model_setup.seg_roi
    rotated_patch_centers = model_setup.rotated_patch_centers
    patch_centers_class = model_setup.patch_centers_class
    sound_model_config = model_setup.sound_model_config
    RANGE_PARAMS = model_setup.range_params
    masses = model_setup.masses
    stiffnesses = model_setup.stiffnesses
    damping = model_setup.damping
    node_info = model_setup.node_info
    scales = session.scales

    # Processing hosts the physical simulation. It is started as its own process
    # group so the complete simulator tree can be terminated during cleanup.
    # Start subprocess in new process group to capture all child processes (Java/Processing)
    simulator_process = session.start_simulator(
        suppress_output=True,
        new_process_group=True,
    )
    print("Starting Sonification. Close the visualization window to end.")
    # Open Viw until user opens sonification window
    session.show_startup_preview(frame_roi, wait_ms=5000)

    # Send the complete model configuration before any frame-level force updates.
    set_sonification_params(sound_model_config, masses, stiffnesses, damping, patch_centers_class.reshape((num_nodes_y, num_nodes_x)))
    send_debug_message("Sonification parameters set.")
    
    print("Beginning Sonification Loop")

    # Preserve initialization geometry throughout the run. Dynamic segmentations
    # update nodes within this common coordinate system rather than redefining it.
    fixed_roi_params = None
    rotation_matrix = M if M is not None else np.eye(2, 3, dtype=np.float32)  # Store rotation matrix or identity

    # Audio recording will start with first frame to sync with video
    prev_frame = frame_0.copy()

    # Initialize timing for class changes
    sonification_start_time = time.time()
    class_changes_log = []

    # Store physical-parameter evolution with the run for later analysis. This is
    # metadata collection; enabling it does not itself send additional audio.
    parameter_evolution = {
        'frame_indices': [],
        'timestamps': [],
        'class_averages': {
            'masses': {},      # {class_id: [values_over_time]}
            'stiffnesses': {},
            'damping': {}
        }
    }

    M_t = M.copy() if M is not None else np.eye(2, 3, dtype=np.float32)
    W,H = frame_0.shape[1], frame_0.shape[0]

    del frame_0_rotated, seg_0_rotated, seg_0_rotated_rgb

    # The same tracker supports segmentation-guided updates and template-based
    # tracking. Its exponential smoothing suppresses isolated tip jumps.
    # === INITIALIZE NEEDLE TIP TRACKER ===
    tracker = NeedleTracker(
        init_frame=frame_0, 
        tip_pos=get_needle_tip_pos_from_seg(seg_img_0 == 1),
        alpha=0.6,  # Same smoothing as tissue_reorient
        init_segmentation=seg_img_0 if "injection" not in folder.lower() else None
    )
    
    print(f"Using {'segmentation' if dynamic_segs else 'template'}-based needle tracking")
    baseline_frame = frame_0.copy()  # Store first frame as baseline for temporal comparison

    # === INITIALIZE SPLINE-BASED LINE FITTING ===
    spline_prior_lines = [ILM_0.copy(), RPE_0.copy()]
    print(f"🔧 Initialized spline-based fitting:")
    print(f"   RPE line: {np.sum(~np.isnan(RPE_0))}/{len(RPE_0)} valid points")
    print(f"   ILM line: {np.sum(~np.isnan(ILM_0))}/{len(ILM_0)} valid points")

    # Spline fitting is more expensive than tracking and visualization, so it is
    # queued in a background worker and consumed asynchronously by the loop.
    # === INITIALIZE BACKGROUND SPLINE FITTING ===
    session.start_spline_worker(debug=False)
    print("🚀 Started background spline fitting thread")
    
    # === INITIALIZE VIDEO WRITER ===
    video_writer = None
    video_frames_buffer = []  # Buffer frames with timestamps
    video_start_time = None
    if save_video:
        print(f"📹 Dynamic framerate video recording enabled")

    frame_last = None

    ex_scale = 1
    audio_started = False  # Flag to track audio recording state
    frame_deformation_history = []
    deformation_norm = 0
    f_ILM, f_RPE = 0.0, 0.0

    # Python is the synchronization clock. Unity should follow the frame_index
    # sent by this object instead of advancing its own timeline.
    sync_sender = SyncSender(ip="127.0.0.1", port=12002)
    capture_name = os.path.basename(folder)
    
    # Smoothing parameters for f_ILM and f_RPE
    f_smoothing_alpha = 0.2  # Lower = more smoothing (0.1 = heavy smoothing, 0.5 = light smoothing)
    
    def smooth_f_values(new_f_ILM, new_f_RPE):
        """Smooth boundary forces and publish them to the timer-visible state."""
        smoothed_f_ilm, smoothed_f_rpe = session.smooth_force_values(
            new_f_ILM,
            new_f_RPE,
            alpha=f_smoothing_alpha,
        )
        with sonification_state['lock']:
            sonification_state['smoothed_f_ILM'] = smoothed_f_ilm
            sonification_state['smoothed_f_RPE'] = smoothed_f_rpe
    
    # Background-region measurements estimate the normal frame-to-frame motion
    # floor. Their running median later becomes an adaptive deformation threshold.
    # Running median filter for deformation-based sonification
    deformation_history = []  # Store recent deformation values for median calculation
    median_filter_size = 30   # Number of frames to consider for median
    
    # Audio workers run at fixed rates and repeatedly use the newest valid state;
    # therefore irregular video processing does not directly modulate audio rate.
    session.start_sonification_timer()
    print("🎵 Sonification timer started - audio synthesis separated from video processing")
    class2_mask = session.class2_mask
    
    while index < len(frame_files):
        frame_file, frame = session.load_frame_at_index(folder, frame_files, index)
        frame_last = frame
        
        if frame is None:
            print(f"Could not load frame: {frame_file}")
            index += 1
            continue

        # Update label if this frame has one
        # if index in labels.values():
            # label = time_to_labels[index]

        # === NEEDLE POSITION TRACKING & DYNAMIC SEGMENTATION ===
        seg_img_current = None
        if remap_with_segmentation or dynamic_segs:
            # Frame and segmentation lists are aligned by index. A missing mask
            # is a hard error because this pipeline has no inference fallback.
            seg_file = os.path.join(seg_files_path, seg_files[index]) if index < len(seg_files) else None
            seg_img_current, global_confidence = session.load_runtime_segmentation(
                seg_file,
                fallback_loader=None,
                cls_to_use=(4 if separate_ilm else 2),
            )
            if seg_img_current is None:
                raise FileNotFoundError(
                    f"Required segmentation could not be loaded for frame: {frame_file}"
                )
        
        frame_file = os.path.join(folder, frame_files[index])
        tracked_tip, max_val = tracker.update(frame, segmentation_map=seg_img_current)
        
        needle_tip_pos = session.filter_tracked_tip(
            tracked_tip,
            max_jump=max_needle_position_jump,
        )
        if needle_tip_pos is None:
            needle_tip_pos = (None, None)

        if debug:
            if needle_tip_pos[0] is not None and needle_tip_pos[1] is not None:
                cv2.circle(frame, (int(needle_tip_pos[0]), int(needle_tip_pos[1])), 8, (0, 255, 0), 2)
                cv2.putText(frame, f"Tip: ({needle_tip_pos[0]:.1f}, {needle_tip_pos[1]:.1f})", 
                           (int(needle_tip_pos[0]) + 10, int(needle_tip_pos[1]) - 10), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)

        closest_node_pos, closest_node_index, dist, current_class = session.find_closest_node_and_class(
            needle_tip_pos,
            rotated_patch_centers,
            patch_centers_class,
        )
        
        if closest_node_index is not None and debug:
            cv2.circle(frame, (int(closest_node_pos[0]), int(closest_node_pos[1])), 6, (0, 255, 0), 2)
        
        class_changed = (prev_class is not None and current_class != prev_class 
                        and [prev_class, current_class] != [4,2] 
                        )
        if class_changed and current_class == 4 and prev_class == 0:
            # A direct background-to-retina jump skips the intervening ILM and is
            # usually a grid-classification artifact. Snap to the nearest ILM row
            # so the anatomical transition remains physically consistent.
            snapped_pos, snapped_index, snapped_dist = session.snap_to_nearest_class_row(
                needle_tip_pos,
                rotated_patch_centers,
                class2_mask,
            )
            if snapped_index is not None:
                row = snapped_index[0]
                closest_node_index = snapped_index
                closest_node_pos = snapped_pos
                current_class = 2
                dist = snapped_dist
                print(f"⚠️  Class 0→4 jump detected — snapped to nearest ILM (class 2) node at row {row}")

        force_info = get_force_sum_for_frame(force_data, index)
            
        # Compute needle-tip velocity as an observable fallback signal when the
        # dataset contains no force measurement for this frame.
        needle_speed = 0.0
        velocity_x = 0.0  # Initialize for use in force proxy calculation
        velocity_y = 0.0  # Initialize for use in force proxy calculation
            
        if session.prev_needle_pos_for_speed is not None and needle_tip_pos[0] is not None:
            prev_pos = session.prev_needle_pos_for_speed
            if prev_pos[0] is not None:
                # Calculate velocity (pixels per frame)
                velocity_x = needle_tip_pos[0] - prev_pos[0]
                velocity_y = needle_tip_pos[1] - prev_pos[1]
                needle_speed = np.sqrt(velocity_x**2 + velocity_y**2)
                    
                # Display speed info if no force data available
                if force_info is None and debug:
                    cv2.putText(frame, f"Needle Speed: {needle_speed:.2f} px/frame", 
                                (10, frame.shape[0] - 60), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 100, 0), 2)
                    cv2.putText(frame, f"Velocity: ({velocity_x:.1f}, {velocity_y:.1f})", 
                                (10, frame.shape[0] - 30), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 100, 0), 2)
            
        # Store current position for next frame
        session.prev_needle_pos_for_speed = needle_tip_pos
        
        # Updating every third frame limits fitting cost. The last accepted
        # splines and node positions remain active between completed updates.
        # === BACKGROUND SPLINE-BASED LINE FITTING ===
        if remap_with_segmentation and index % 3 == 0 and seg_img_current is not None:
            task_queued = session.queue_spline_task(index, seg_img_current)
            if debug and task_queued:
                print(f"📤 Frame {index}: Submitted spline fitting task to background thread")
            if debug and not task_queued:
                print(f"⚠️  Frame {index}: Spline fitting queue full, skipping this frame")
        
        # Consume only recent results so delayed worker output cannot move the
        # physical grid back toward anatomy from a much older frame.
        for result in session.collect_recent_spline_results(index, max_age=10):
            result_frame = result['frame_index']
            seg_img_current = result['fitted_seg']
            
            spline_prior_lines = [result['updated_lines']['ILM'].copy(), result['updated_lines']['RPE'].copy()]
            ILM_line, RPE_line = spline_prior_lines
                    
            if debug:
                print(f"📥 Frame {index}: Applied spline result from frame {result_frame}")
                print(f"   RPE confidence: {result['confidences']['RPE']:.3f}")
                print(f"   ILM confidence: {result['confidences']['ILM']:.3f}")
            
            node_update_rate = 0.99
            rotated_patch_centers, ILM_line, RPE_line, confidence = session.apply_spline_result_to_patch_centers(
                result,
                rotated_patch_centers,
                node_info,
                current_class,
                gamma=node_update_rate,
            )

            if np.isnan(confidence):
                confidence = 0.5
                print(f"⚠️  NaN confidence detected, using default 0.5")
            
            # Low segmentation confidence is represented acoustically as stronger
            # simulator jitter. Reliable masks therefore produce a steadier model.
            if use_confidence_weights:
                C_min = 0.5
                print(global_confidence )
                u = np.clip((C_min - global_confidence) / C_min, 0.0, 1.0)
                ex_scale = max(0.2, (1 - u)**2) 
                mult = 1 + (20 * u**2)  
                mult = min(mult, 20.0)  
                
                jitter_rate = u**2 * JITTER_MAX_HZ
                jitter_amplitude = 0.1 + u * 0.8
                jitter_cutoff = 0.2 + u * 0.3
                
                transmitter.oscTransmittProc_SetJitter(jitter_rate, jitter_amplitude, jitter_cutoff)
            # Rotate the fitted segmentation into the initialization coordinate
            # system before comparing boundary motion and retinal thickness.
            # === DEFORMATION-BASED MEASUREMENT IN ROTATED FRAME ===
            rotated_seg = result['fitted_seg']
            if M_t is not None:
                rotated_seg = cv2.warpAffine(rotated_seg, M_t, (rotated_seg.shape[1], rotated_seg.shape[0]), flags=cv2.INTER_NEAREST)
            
            ILM_line_rotated = extract_line(rotated_seg, ILM_LABEL)
            RPE_line_rotated = extract_line(rotated_seg, RPE_LABEL)
            
            # Calculate thickness in rotated (horizontal-tissue) coordinate system
            current_thickness_rotated = RPE_line_rotated - ILM_line_rotated
            
            # Transform needle tip position to rotated coordinates for consistent window calculation
            if needle_tip_pos[0] is not None and M_t is not None:
                needle_point = np.array([[needle_tip_pos[0], needle_tip_pos[1]]], dtype=np.float32)
                needle_rotated = cv2.transform(needle_point.reshape(1, 1, 2), M_t).reshape(2)
                needle_x_rotated = int(needle_rotated[0])
            else:
                needle_x_rotated = int(needle_tip_pos[0]) if needle_tip_pos[0] is not None else 40
            
            # Use rotated coordinates for window calculation
            context_window = 40
            needle_window_rotated = [needle_x_rotated - context_window, needle_x_rotated + context_window]
            needle_window_rotated[0] = max(0, needle_window_rotated[0])
            needle_window_rotated[1] = min(len(ILM_line_rotated), needle_window_rotated[1])
            
            # The selected statistic controls robustness versus sensitivity of
            # local boundary displacement within the needle-centered window.
            delta_ILM, delta_RPE = None, None
            if thickness_statistic == 'median':
                delta_ILM = np.nanmedian(np.abs(ILM_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]] - prev_ILM_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]]))
                delta_RPE = np.nanmedian(np.abs(RPE_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]] - prev_RPE_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]]))
            elif thickness_statistic == 'mean':
                delta_ILM = np.nanmean(np.abs(ILM_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]] - prev_ILM_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]]))
                delta_RPE = np.nanmean(np.abs(RPE_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]] - prev_RPE_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]]))
            elif thickness_statistic == 'max':
                delta_ILM = np.nanmax(np.abs(ILM_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]] - prev_ILM_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]]))
                delta_RPE = np.nanmax(np.abs(RPE_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]] - prev_RPE_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]]))
            elif thickness_statistic == '95th_percentile':
                delta_ILM = np.nanpercentile(np.abs(ILM_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]] - prev_ILM_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]]), 95)
                delta_RPE = np.nanpercentile(np.abs(RPE_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]] - prev_RPE_line_rotated[needle_window_rotated[0]:needle_window_rotated[1]]), 95)
            
            if np.isnan(delta_ILM):
                delta_ILM = 0.0
            if np.isnan(delta_RPE):
                delta_RPE = 0.0
            
            delta_thickness_rotated = np.nanmedian(np.abs(current_thickness_rotated[needle_window_rotated[0]:needle_window_rotated[1]] - prev_thickness_rotated[needle_window_rotated[0]:needle_window_rotated[1]]))
            if np.isnan(delta_thickness_rotated):
                delta_thickness_rotated = 0.0
            
            deformation_raw = 1.0 * delta_thickness_rotated

            # Background frames populate the noise baseline instead of producing
            # deformation audio. At least five samples are required before the
            # adaptive median threshold replaces the conservative startup value.
            if current_class == 0:
                deformation_history.append(deformation_raw)
                if len(deformation_history) > median_filter_size:
                    deformation_history = deformation_history[-median_filter_size:]
                if debug and deformation_raw > 0.1:
                    print(f"📊 Baseline deformation recorded: {deformation_raw:.3f} (history size: {len(deformation_history)})")
            
            if len(deformation_history) >= 5:
                current_median = np.median(deformation_history)
                threshold_multiplier = 1.2
                calculated_threshold = current_median * threshold_multiplier
                
                min_threshold = 0.3
                sonification_threshold = max(calculated_threshold, min_threshold)
                
                if deformation_raw > sonification_threshold and current_class != 0:
                    f_ILM = -np.clip(deformation_raw, 0, 2)
                    print(f"🎵 f_ILM TRIGGERED (rotated): deformation={deformation_raw:.3f} > threshold={sonification_threshold:.3f} (baseline_median={current_median:.3f})")
                else:
                    f_ILM = 0.0
                    if current_class != 0 and deformation_raw > 0.1:
                        print(f"🔇 f_ILM FILTERED (rotated): deformation={deformation_raw:.3f} <= threshold={sonification_threshold:.3f} (baseline_median={current_median:.3f})")
                
                if current_median < 0.1 and debug:
                    print(f"⚠️  Low baseline detected: median={current_median:.3f}, using min_threshold={min_threshold:.3f}")
            else:
                f_ILM = -np.clip(deformation_raw, 0, 2) if deformation_raw > 0.5 and current_class != 0 else 0.0
                if len(deformation_history) < 5 and current_class == 0:
                    print(f"📈 Collecting baseline data: {len(deformation_history)}/5 samples needed")
            if "injection" not in folder.lower():
                f_ILM *= 0.05
            
            prev_thickness_rotated = current_thickness_rotated.copy()
            prev_ILM_line_rotated = ILM_line_rotated.copy()
            prev_RPE_line_rotated = RPE_line_rotated.copy()
        
        # IMPORTANT: this is currently a constant test force, scaled by anatomy
        # and update rate. It does not yet use ``force_info`` or ``needle_speed``.
        magnitudes = np.ones_like([0,0,0]) * 1 * (20/sonification_rate) * scales[current_class] # TEMPORARY FIX TO CONSTANT FORCE FOR TESTING
        
        
        deflection_scaling = 1.0

        # Distribute the three-component input around the closest grid node with
        # a Gaussian falloff, producing the A-scan force vector sent by the timer.
        node_magnitudes = compute_node_magnitudes(num_nodes_y, magnitudes,
                                                closest_node_index, dist, deflection_scaling, sigma=3)

        # Emphasize real anatomical transitions with a brief magnitude increase
        # and log them relative to the recording start for later inspection.
        if class_changed:
            print(f"⚡ Class change detected: {prev_class} → {current_class}")
            node_magnitudes *= 2.0
            region_cue = REGION_CRACKLE_CUES.get(current_class)
            if region_cue is not None:
                set_crackle_params(*region_cue)
            class_changes_log.append(
                session.create_class_change_record(
                    index,
                    prev_class,
                    current_class,
                    needle_tip_pos,
                    sonification_start_time,
                )
            )
            
        prev_class = current_class
        
        # Publish one internally consistent snapshot. Audio callbacks copy these
        # values under the same lock and never read half-updated frame data.
        with sonification_state['lock']:
            sonification_state['latest_node_magnitudes'] = node_magnitudes.copy()
            sonification_state['latest_current_class'] = current_class
            sonification_state['latest_f_ILM'] = f_ILM
            sonification_state['latest_f_RPE'] = f_RPE
            sonification_state['latest_needle_pos'] = needle_tip_pos
            sonification_state['roi_bounds'] = (x0, y0, side_x, side_y) if 'x0' in locals() else None
            sonification_state['latest_dist_to_anatomy'] = abs(dist) if dist != 0 else float('inf')
            sonification_state['sonification_ready'] = True  # Mark that we have valid data
        
        # The helper acquires the lock itself, so call it after releasing the
        # snapshot lock to avoid a non-reentrant-lock deadlock.
        smooth_f_values(f_ILM, f_RPE)

        # Send the same frame index that the sonification loop is using.
        # This is the important synchronization signal: Unity can display
        # exactly this frame instead of running an independent timer.
        sync_depth = abs(dist) if dist is not None and np.isfinite(dist) else None
        sync_tension = min(abs(float(f_ILM)) / 2.0, 1.0)
        safe_sync_send(
            sync_sender,
            frame_index=index,
            timestamp_seconds=index / SYNC_FPS,
            capture_name=capture_name,
            needle_tip=valid_sync_tip(needle_tip_pos),
            injection_depth=sync_depth,
            injection_depth_unit="voxel",
            tension_normalized=sync_tension,
            warning_state="warning" if abs(float(f_ILM)) > 0.0 else "none",
        )
        
        ## Other frame info for debugging
        # draw_information(frame,
        #                  index,
        #                  frame_files,
        #                  label,
        #                  force_data,
        #                  show_force,
        #                  rotated_patch_centers,
        #                  patch_centers_class,
        #                  debug=debug
        # )
        

        ## CONTROLS
        cv2.imshow('Video Visualization Tool', frame)
        # Show overlay of segmentation: ILM is yellow, RPE is blue and needle is red
        # Everything else is 0 (black)
        display_frame = frame  # Default to original frame
        # if seg_img_orig is not None:
        #     overlay = np.zeros_like(frame)
        #     overlay[seg_img_orig == ILM_LABEL] = (0, 255, 255)  # Yellow for ILM
        #     overlay[seg_img_orig == RPE_LABEL] = (255, 0, 0)    # Blue for RPE
        #     if needle_tip_pos[0] is not None and needle_tip_pos[1] is not None:
        #         cv2.circle(overlay, (int(needle_tip_pos[0]), int(needle_tip_pos[1])), 8, (0, 0, 255), -1)  # Red for needle tip
        #     blended = cv2.addWeighted(frame, 0.7, overlay, 0.3, 0)
        #     display_frame = blended  # Use blended frame for both display and recording
        #     cv2.imshow('Video Visualization Tool', blended)
        
        # Start audio on the first buffered video frame so both recordings share
        # the same practical time origin. Timestamps retain irregular frame timing.
        if save_video:
            video_start_time, audio_started = session.buffer_video_frame(
                video_frames_buffer,
                display_frame,
                video_start_time,
                audio_started=audio_started,
                start_audio_callback=start_recording,
            )
        
        # Handle controls and get updated state
        control_result = handle_video_controls(
            paused, index, len(frame_files), show_force
        )
        
        # Unpack results
        should_exit = control_result['exit']
        paused = control_result['paused']
        index = control_result['index']
        show_force = control_result['show_force']
        
        # Break if exit was requested
        if should_exit:
            break
    
    cv2.destroyAllWindows()
    
    # Stop live producers before writing run artifacts. This prevents callbacks
    # from changing state while metadata and recordings are being finalized.
    if audio_started:
        stop_recording()

    # Clean up sonification timer
    session.stop_sonification_timer()

    # Mark the stream as complete and release the UDP socket. This is placed
    # after the loop cleanup so Unity is notified when playback reaches the end
    # or when the user exits the OpenCV window.
    try:
        sync_sender.send_end(
            capture_name=capture_name,
            final_frame_index=index,
        )
    finally:
        sync_sender.close()
    
    # Capture the effective configuration and initialized model parameters, not
    # merely the command-line defaults, so a run can be inspected afterward.
    folder_name = os.path.basename(folder)
    config = SonificationConfig(
        script_type=folder_name,  # Use folder name instead of generic tracking type
        num_nodes_x=num_nodes_x, num_nodes_y=num_nodes_y,
        extend_roi_to_needle_tip=extend_roi_to_needle_tip,
        add_margin_to_roi=add_margin_to_roi,
        sep_f_components=sep_f_components,
        remap_with_segmentation=remap_with_segmentation,
        use_handle_forces=use_handle_forces,
        static_mapping_type=static_mapping_type,
        dynamic_detuning=dynamic_detuning,
        use_deflection_scaling=use_deflection_scaling,
        deflection_debug=deflection_debug,
        separate_ilm=separate_ilm,
        refine_with_sam=refine_with_sam,
        ranges=ranges,
        simulator_path=simulator_path,
        use_dynamic_intensity_mapping=use_dynamic_intensity_mapping,
        # Pass computed data with appropriate defaults
        has_needle=True,  # Manual tracking assumes needle presence
        is_synthetic=False,  # Manual tracking uses real data
        ROI=ROI if 'ROI' in locals() else None,
        angle=0,  # Default angle
        needle_tip_pos=needle_tip_pos if 'needle_tip_pos' in locals() else (None, None),
        RANGE_PARAMS=RANGE_PARAMS if 'RANGE_PARAMS' in locals() else None,
        masses=masses if 'masses' in locals() else None,
        stiffnesses=stiffnesses if 'stiffnesses' in locals() else None,
        damping=damping if 'damping' in locals() else None,
        seg_img_0=seg_img_0 if 'seg_img_0' in locals() else None,
        patch_centers_class=patch_centers_class if 'patch_centers_class' in locals() else None,
        parameter_evolution=parameter_evolution if 'parameter_evolution' in locals() else None
    )
    
    run_folder_path = create_run_folder_and_save_data(
        folder="inference",
        sonification_start_time=sonification_start_time,
        class_changes_log=class_changes_log,
        sound_model_config=sound_model_config if 'sound_model_config' in locals() else None,
        config=config,
        sample_name=folder.split(os.sep)[-1]
    )
    
    # OpenCV writes a constant-rate MP4. The rate is estimated from observed
    # timestamps, while the accompanying JSON preserves the original timing.
    if save_video and len(video_frames_buffer) > 1:
        print(f"🎬 Creating synced video with {len(video_frames_buffer)} frames...")
        
        # Calculate dynamic framerate based on actual viewing time
        total_time = video_frames_buffer[-1][1] - video_frames_buffer[0][1]
        if total_time > 0:
            avg_framerate = len(video_frames_buffer) / total_time
            # Clamp framerate to reasonable bounds
            avg_framerate = max(5.0, min(avg_framerate, 60.0))
        else:
            avg_framerate = 30.0
        
        # Get video dimensions from first frame
        height, width = video_frames_buffer[0][0].shape[:2]
        video_filename = os.path.join(run_folder_path, 'simulation_video.mp4')
        fourcc = cv2.VideoWriter_fourcc(*'mp4v')
        video_writer = cv2.VideoWriter(video_filename, fourcc, avg_framerate, (width, height))
        
        # Write all buffered frames
        for frame, timestamp in video_frames_buffer:
            video_writer.write(frame)
        
        video_writer.release()
        
        # Save timing metadata for audio synchronization
        timing_file = os.path.join(run_folder_path, 'video_timing.json')
        timing_data = {
            'total_frames': len(video_frames_buffer),
            'total_duration_seconds': total_time,
            'average_framerate': avg_framerate,
            'frame_timestamps': [timestamp for _, timestamp in video_frames_buffer],
            'video_filename': 'synced_visualization.mp4'
        }
        
        import json
        with open(timing_file, 'w') as f:
            json.dump(timing_data, f, indent=2)
        
        print(f"📹 Synced video saved: {video_filename}")
        print(f"⏱️  Video duration: {total_time:.2f}s at {avg_framerate:.1f} FPS")
        print(f"📋 Timing data saved: {timing_file}")
    elif save_video:
        print("⚠️ No video frames recorded or insufficient frames for video creation")
    
    # The spline worker is independent of the audio timers and must be stopped
    # separately after its results are no longer needed.
    print("🛑 Stopping background spline fitting thread...")
    try:
        session.stop_spline_worker(timeout=2.0)
    except Exception as e:
        print(f"⚠️  Warning during thread cleanup: {e}")

    # Allow the audio backend to flush its WAV before terminating Processing and
    # moving the recording from its working location into the run directory.
    sleep(2)
    
    # Processing may spawn Java children, so use the process-tree-aware cleanup
    # helper instead of terminating only the parent handle.
    if simulator_process is not None:
        kill_process_processing(simulator_process)
    
    # Move recording
    recording_source = os.path.join(
        os.getcwd(),
        "inference",
        "recording.wav",
    )

    move_recording_to_run(
        recording_source,
        run_folder_path,
    )

    
    # Run ACE

    return run_folder_path  # Return the path for potential further use


if __name__ == "__main__":
    # Single mode processes one subject/scan path. Batch mode discovers subject
    # folders containing the expected ``iOCT Microscope/Volume`` data layout.
    parser = argparse.ArgumentParser(description="Video Visualization Tool")
    parser.add_argument("--folder_mode", default="single", help="Mode: 'single' for single folder, 'batch' for batch processing")
    parser.add_argument("root_folder", help="Path to the root folder containing video frame subfolders")
    parser.add_argument("--refine_with_sam", nargs='+', help="Masks to refine")
    parser.add_argument("--use_dynamic_intensity_mapping", action="store_true", default=False,  # Changed to False for performance
                       help="Enable dynamic intensity-based mass mapping for deformation capture")
    parser.add_argument("--ranges", default="SETUP_SPLINES", help="Parameter ranges configuration")
    parser.add_argument("--dynamic_segs", action="store_true", default=True,
                       help="Use segmentation-based needle tracking instead of template matching")
    parser.add_argument("--simulator_path", default="")
    parser.add_argument("--save_video", action="store_true", default=True,
                       help="Save the visualization as an MP4 video file")
    parser.add_argument("--sonification_rate", type=float, default=25.0,
                       help="A-scan sonification frequency in Hz (default: 25.0)")
    parser.add_argument("--ilm_rpe_rate", type=float, default=12.0,
                       help="ILM/RPE boundary sonification frequency in Hz (default: 10.0)")
    parser.add_argument("--max_needle_jump", type=float, default=100.0,
                       help="Maximum allowed needle position jump in pixels to prevent artifacts from noise (default: 100.0)")
    args = parser.parse_args()
    
    if args.folder_mode == "single":
        parameterize_and_sonify_oct(
            args.root_folder, 
            refine_with_sam=args.refine_with_sam or [],
            use_dynamic_intensity_mapping=args.use_dynamic_intensity_mapping,
            ranges=args.ranges,
            dynamic_segs=args.dynamic_segs,
            simulator_path=args.simulator_path,
            save_video=args.save_video,
            sonification_rate=args.sonification_rate,
            ilm_rpe_rate=args.ilm_rpe_rate,
            max_needle_position_jump=args.max_needle_jump
        )
    else:
        for folder_name in sorted(os.listdir(args.root_folder)):
            folder_path = os.path.join(args.root_folder, folder_name)
            volume_path = os.path.join(folder_path, "iOCT Microscope", "Volume")
            if os.path.isdir(volume_path):
                parameterize_and_sonify_oct(
                    folder_path, 
                    refine_with_sam=args.refine_with_sam or [],
                    use_dynamic_intensity_mapping=args.use_dynamic_intensity_mapping,
                    ranges=args.ranges,
                    dynamic_segs=args.dynamic_segs,
                    simulator_path=args.simulator_path,
                    save_video=args.save_video,
                    sonification_rate=args.sonification_rate,
                    ilm_rpe_rate=args.ilm_rpe_rate,
                    max_needle_position_jump=args.max_needle_jump
                )
                sleep(5)
