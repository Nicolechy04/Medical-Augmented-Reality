# Medical Augmented Reality Practical Course

## Subretinal Needle Injection

This repository contains a medical augmented reality application developed as part of the **Medical Augmented Reality Practical Course (SoSe 2026)** at the Technical University of Munich (TUM).

The project explores visualization and sonification techniques for augmenting a subretinal needle injection procedure. Its goal is to provide additional real-time information about the surgical scene, needle position, anatomical structures, and tissue interaction.

## Team

- Chua Hui Ying Nicole
- Salim Daoud
- Jeongjwon Na
- Elena Reinbold Fraire

## Project Components

The project currently consists of three main components:

- **Sonification:** Converts information from iOCT images and segmentations into auditory feedback.
- **Visualization:** Provides visual augmented-reality feedback for the procedure.
- **Synchronization:** Sends frame-indexed state from the sonification to the visualization components so that they can run simultaneously.

## Repository Structure

```text
.
├── sonification/
│   ├── sonify_core/
│   │   ├── distance_ioct_sonification.py
│   │   ├── ioct_sonification.py
│   │   ├── ioct_sonification_base.py
│   │   ├── dist_sonification_main.scd
│   │   └── ...
│   ├── processing_sonobox/
│   └── requirements.txt
│
├── visualization/
│   └── ...
│
├── synchronization/
│   ├── sync_sender.py
│   ├── sync_state.py
│   └── ...
│
└── README.md
```

The main directories are:

- `sonification/`: Distance-based and physics-based sonification pipelines.
- `visualization/`: Visualization and augmented-reality components.
- `synchronization/`: Communication between the Python sonification pipeline and the visualization application.

Generated files, recordings, datasets, and Python environments should not be committed to the repository.

## Expected Dataset Structure

A complete recording, for example `Subretinal Injection 1`, represents one temporal capture.

```text
Subretinal Injection 1/
├── Canvas/
│   ├── 00000.png
│   ├── 00001.png
│   └── ...
│
├── Numerical/
│   ├── 00000.json
│   ├── 00001.json
│   └── ...
│
├── Stereo Left/
│   ├── 00000/
│   ├── 00001/
│   └── ...
│
└── iOCT Microscope/
    ├── properties.json
    └── Volume/
        ├── 00000/
        │   ├── 00000.png
        │   ├── ...
        │   ├── 00256.png
        │   ├── ...
        │   ├── 00511.png
        │   └── Segmentation/
        │       ├── 00000.png
        │       ├── ...
        │       ├── 00256.png
        │       └── ...
        │
        ├── 00001/
        └── ...
```

Each numbered folder under `Volume/` is one temporal time point containing a three-dimensional OCT volume. The current pipelines select the same representative B-scan, `00256.png`, from every volume.

For example:

```text
Time point 0  → Volume/00000/00256.png
Time point 1  → Volume/00001/00256.png
Time point 2  → Volume/00002/00256.png
```

The corresponding segmentation is loaded from:

```text
Volume/<time-point>/Segmentation/00256.png
```

Every selected OCT frame must have a corresponding segmentation. The pipelines stop with an error if a required segmentation is missing or cannot be read.

### Segmentation Labels

The segmentation masks use the following integer labels:

| Label | Structure |
|------:|-----------|
| `0` | Background / vitreous |
| `1` | Needle |
| `2` | Internal limiting membrane (ILM) |
| `3` | Retinal pigment epithelium (RPE) |

Additional intermediate labels may be produced internally during postprocessing but are not required in the supplied segmentation masks.

## Sonification

The project currently supports two sonification pipelines:

1. **Distance-based sonification**
2. **Physics-based sonification with additional distance cues**

Both pipelines use the same OCT frame sequence, segmentation masks, needle tracking, and shared preprocessing functionality.

### Python Setup

From the `sonification/` directory, create and activate a virtual environment.

On Windows:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
```

On macOS or Linux:

```bash
python3 -m venv .venv
source .venv/bin/activate
```

Install the required Python packages:

```bash
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

### Distance-Based Sonification

The distance-based pipeline maps the position of the needle relative to the segmented retinal layers to auditory parameters.

The feedback includes:

- Pulse frequency based on the distance to the next anatomical boundary.
- Base frequency based on the current anatomical region.
- Timbre changes based on the detected tissue class.
- Additional modulation based on estimated tissue deformation.

The audio is generated in SuperCollider and controlled from Python through OSC messages.

#### Starting SuperCollider

1. Open the following file in SuperCollider:

```text
sonify_core/dist_sonification_main.scd
```

2. Evaluate the file.
3. Wait for the following readiness message:

```text
[SC] Ready for unified sonification + recording
```

#### Running the Distance-Based Pipeline

From the `sonification/` directory on Windows:

```powershell
python .\sonify_core\distance_ioct_sonification.py "C:\path\to\Subretinal Injection 1"
```

On macOS or Linux:

```bash
python sonify_core/distance_ioct_sonification.py "/path/to/Subretinal Injection 1"
```

The dataset argument must point to the capture root, not directly to the `Volume/` directory.

#### Distance-Based Output

After execution, a timestamped directory is created under:

```text
sonification/runs/
```

Depending on the enabled options, it contains:

```text
runs/
└── distance_based_<capture-name>_<timestamp>/
    ├── recording.wav
    ├── simulation_video.mp4
    ├── video_timing.json
    └── class_changes_log.json
```

The generated files are:

- `recording.wav`: Recorded sonification audio.
- `simulation_video.mp4`: Recorded OCT playback.
- `video_timing.json`: Frame timestamps and video timing information.
- `class_changes_log.json`: Session metadata and detected class transitions.

The `runs/` directory contains generated output and should not be committed.

### Physics-Based Sonification

The physics-based pipeline constructs a physical model of the segmented retinal region. It maps image, segmentation, deformation, and needle-interaction information to parameters such as:

- Mass
- Stiffness
- Damping
- Excitation forces

Python performs the image processing, tracking, mapping, and model control. The SonoBox Processing application generates the physics-based audio.

#### Starting SonoBox

The SonoBox source is located in:

```text
sonification/processing_sonobox/
```

Open and run the project using Processing before starting the Python pipeline.

If a compiled SonoBox executable is available, its path can instead be supplied using `--simulator_path`.

#### Running the Physics-Based Pipeline

With SonoBox already running, execute the following command from the `sonification/` directory.

On Windows:

```powershell
python .\sonify_core\ioct_sonification.py "C:\path\to\Subretinal Injection 1"
```

To let Python start a compiled SonoBox executable:

```powershell
python .\sonify_core\ioct_sonification.py `
    "C:\path\to\Subretinal Injection 1" `
    --simulator_path "C:\path\to\Sonobox.exe"
```

On macOS or Linux:

```bash
python sonify_core/ioct_sonification.py \
    "/path/to/Subretinal Injection 1" \
    --simulator_path "/path/to/Sonobox"
```

#### Physics-Based Output

The physics-based pipeline creates a timestamped run directory containing:

```text
runs/
└── physics_based_<capture-name>_<timestamp>/
    ├── recording.wav
    ├── simulation_video.mp4
    ├── video_timing.json
    └── class_changes_log.json
```

Additional model parameters or diagnostic information may also be stored depending on the active configuration.

## Visualization

The visualization component provides augmented visual feedback for the subretinal injection procedure.

## Synchronization

The synchronization component exchanges frame-indexed state between Python and the visualization application.

The relevant source files are located in:

```text
synchronization/
```

The Python sonification pipelines act as the synchronization clock. During playback, they send information including:

- Frame index
- Capture name
- Timestamp
- Needle-tip position
- Estimated injection depth
- Warning state
- Additional procedure-specific values

The default synchronization endpoint uses OSC on:

```text
IP:   127.0.0.1
Port: 12002
```

The sonification pipelines can still run when no visualization receiver is active. In that case, synchronization messages are sent but not consumed.

Further setup instructions for the visualization receiver will be added when that component is finalized.

## Communication Overview

The current components use the following local OSC connections:

| Sender | Receiver | Default port | Purpose |
|--------|----------|-------------:|---------|
| Distance Python pipeline | SuperCollider | `57120` | Distance-based audio parameters and recording control |
| Physics Python pipeline | SonoBox | `12001` | Physical-model parameters, excitation, and recording control |
| Python sonification pipeline | Visualization application | `12002` | Frame-indexed synchronization state |

## Notes

- Datasets are not included in this repository.
- Generated recordings and videos are not intended to be committed.
- The supplied segmentation masks are required; automatic fallback inference is not included.
- Start the required audio engine before running its corresponding Python pipeline.
- Use the capture root as the dataset argument.