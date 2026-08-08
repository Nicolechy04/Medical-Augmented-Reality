# Medical Augmented Reality — Visualization Branch

## Main Scene

**`Assets/Scenes/BaseVisualization_v04.unity`** — open this scene to run the full visualization.

> All other scenes (`InjectionAngleDemo`, `TensionDemo`, `InjectionAngle3DDemo`, etc.) are earlier prototypes kept for reference.

---

## Features

### 1. iOCT 3D Volume Rendering
- Loads the `iOCT Microscope/Volume/` subfolders frame-by-frame.  
- Each frame contains 513 B-scan PNG slices that are assembled into a `Texture3D` and rendered via direct volume rendering (DVR).  
- Configurable B-scan step (`BscanStep` in Inspector) — step 4 (128 slices) is recommended for performance.

### 2. 2D Canvas Overlay
- Displays the corresponding `Canvas/{frame}.png` enface image alongside the 3D volume.

### 3. Stereo-Left Microscope View
- Loads `Stereo Left/{frame}/microscope.png` and aligns it to the OCT crosshair anchors in world space.

### 4. Tissue Tension Heatmap
- Compares each frame's OCT volume against the frame-0 baseline to produce a colour-coded tension overlay, masking out needle-tip voxels via the segmentation so cannula reflections don't read as tissue deformation.
- Tension ramps up while the needle is advancing and decays back down while it's retracting (directional smoothing), instead of tracking the raw per-voxel diff 1:1.
- **Green/blue** = low tension, **Red** = high tension.
- Driven by `TensionHeatmapController.cs`.
- The tension level is also shown in a small top-right UI bar (`TensionGaugeUI.cs`).
- By default the displayed value is Unity's own local OCT-difference calculation above. `IOCTSyncToTension.cs` can optionally override it with the physics-sonification pipeline's live `tension_normalized` value from Python (toggle **Use Python Tension** on that component) when running with live sync.

### 5. Injection Angle Safety Ring (`InjectionRing3D.cs`)
A holographic ring rendered at the needle tip in 3D space that gives the surgeon real-time depth and angle feedback. Ring colour is driven primarily by **approach angle**, with a distinct cyan state once the needle is confirmed inside the subretinal space:

| Ring Colour | Meaning |
|---|---|
| 🔘 Grey | Needle genuinely not detected/withdrawn (see below) |
| 🟢 Green | Safe approach angle (43°–47° from vertical) |
| 🟡 Amber | Caution — borderline angle |
| 🔴 Red | Danger — angle too steep or too shallow |
| 🩵 Cyan (blended with angle colour) | Needle confirmed inside the subretinal space — still tinted green/amber/red so a dangerous angle stays visible even once past the ILM |

Details:
- **Angle is the default signal at every depth**, not just while approaching. Green/amber/red always reflects the current needle angle; cyan (subretinal space) is layered on top as a 50/50 blend with the angle colour, rather than replacing it outright.
- Subretinal space is decided by the **real per-frame `ILM Distance` measurement** (`ilmDist < 0`) from the dataset JSON — not an assumed fixed depth for where the retina sits in the scan volume.
- If `ILM Distance` reads as the "lost" sentinel for a frame (see JSON Format below) but the needle tip itself is still tracked, the ring keeps showing the angle colour rather than misreporting cyan or grey — a lost ILM reading only means "can't confirm subretinal space," not "needle is gone."
- **Grey only means the needle tip itself is genuinely withdrawn** (3D tip sentinel, `CannulaTipDepthMM` far below any legitimate reading — tunable via `WithdrawnDepthThreshold`, default `-500mm`). It requires several consecutive frames below threshold before triggering (`WithdrawnHoldFrames`, default `5`), so a single noisy/transient reading doesn't flip the ring early.
- The outer compass ring shows a **white dot** indicating the needle's real entry direction (no clock-hand line). It hides when the 2D `Cannula SRI` keypoint tracker can't find the cannula in frame, also with a consecutive-frame hold (`AngleLostHoldFrames`, default `5`) so a single dropped tracking frame doesn't cause visible flicker.
- Whole ring (and outer compass) hides automatically until the needle tip is first detected in the sequence.

### 6. Injection Angle Gauge (`AngleGaugeUI.cs`)
- Top-right UI bar showing the computed needle angle (degrees from vertical) and its safety zone.
- Shows `Angle --` whenever the ring is hidden (no needle detected).
- Angle is computed directly from the 2D `Cannula SRI` keypoints in each frame's JSON — does **not** depend on ILM tracking.

---

## Data Folder Structure

The player auto-detects the data root. Place the dataset folder (named **`Subretinal Injection 1`**) either:
- **Next to the Unity project folder** (i.e. one level above `Assets/`), or  
- **One level above that** (the player will search up the directory tree).

You can also set the path manually in the **SubretinalSequencePlayer Inspector** field `Data Root Path`.

Expected layout inside the data root:

```
Subretinal Injection 1/
├── iOCT Microscope/
│   └── Volume/
│       ├── 00000/          ← one folder per frame
│       │   ├── 00000.png   ← B-scan slices (513 total)
│       │   ├── 00001.png
│       │   └── ...
│       ├── 00001/
│       └── ...
├── Canvas/
│   ├── 00000.png           ← enface canvas image per frame
│   └── ...
├── Stereo Left/
│   ├── 00000/
│   │   └── microscope.png  ← top-down camera image per frame
│   └── ...
└── Numerical/
    ├── 00000.json          ← per-frame tracking data (tip 3D, keypoints, ILM/RPE distance)
    └── ...
```

### JSON Format (per frame)
Each `Numerical/{frame}.json` provides:
- `Surgical Tool > CANNULA > Spatial > Tip` — 3D needle tip position (mm)
- `Surgical Tool > CANNULA > Meta > ILM Distance` — distance from tip to ILM surface (mm); `3.4e38` = sentinel / lost. `SubretinalSequencePlayer.cs` additionally treats any parsed value over `100mm` as lost too (a sanity clamp, since the scan volume itself is only ~40mm deep).
- `Surgical Tool > CANNULA > Meta > RPE Distance` — distance from tip to RPE surface (mm)
- `Keypoints > Cannula SRI > Tip / Start` — 2D pixel coordinates in the 2048×2048 microscope image (used to compute injection angle)
- `iOCT Microscope > Spatial > Translation / Rotation` — scanner world pose (for 3D alignment)
- `OCT Crosshair` — crosshair line endpoints for 2D↔3D plane alignment

---

## Key Scripts

| Script | Purpose |
|---|---|
| `SubretinalSequencePlayer.cs` | Master frame loader — volumes, images, JSON parsing, 2D/3D alignment |
| `InjectionRing3D.cs` | Holographic safety ring + outer compass ring at needle tip |
| `AngleGaugeUI.cs` | Top-right angle bar UI |
| `TensionHeatmapController.cs` | OCT difference heatmap computation and GPU upload |
| `TensionGaugeUI.cs` | Tension level bar UI |
| `IOCTSyncToTension.cs` | Optionally overrides the local tension value with Python's live `tension_normalized` from the physics-sonification OSC stream |
| `OctRawCache.cs` | Disk cache for decoded B-scan volumes (avoids re-parsing PNGs) |
| `RealInjectionAngleController.cs` | 2D angle overlay on the enface canvas |
| `IOCTOscReceiver.cs` | OSC listener for live iOCT sync (used in full system integration) |

---

## Setup in Unity

1. Open **`Assets/Scenes/BaseVisualization_v04.unity`**.
2. In the Hierarchy, select the **SubretinalSequencePlayer** GameObject.
3. In the Inspector, set **Data Root Path** to your local `Subretinal Injection 1` folder (or leave blank to auto-detect).
4. Set **Bscan Step** to `4` for real-time performance (`1` for full quality).
5. Press **Play**.

Use the **frame slider** or **Play/Pause** buttons in the Game view to step through the injection sequence.

---

## Dependencies

- Unity **2022.3 LTS** or newer
- No external packages required (all shaders and scripts are self-contained)
