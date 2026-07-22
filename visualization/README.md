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
- Compares each frame's OCT volume against the frame-0 baseline to produce a colour-coded tension overlay.
- **Green** = low tension, **Red** = high tension.
- Driven by `TensionHeatmapController.cs`.
- The tension level is also shown in a small top-right UI bar (`TensionGaugeUI.cs`).

### 5. Injection Angle Safety Ring (`InjectionRing3D.cs`)
A holographic ring rendered at the needle tip in 3D space that gives the surgeon real-time depth and angle feedback:

| Ring Colour | Meaning |
|---|---|
| 🔘 Grey | Standby — needle not detected or retracted |
| 🟢 Green | Safe approach angle (43°–47° from vertical) |
| 🟡 Amber | Caution — borderline angle |
| 🔴 Red | Danger — angle too steep or too shallow |
| 🩵 Cyan | Needle is inside the subretinal space |

- The outer compass ring shows a **white dot** indicating the needle's real entry direction (no clock-hand line).
- Ring hides automatically when the needle is not detected (3D tip sentinel `Y > 500` or 2D keypoints out of bounds).

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
- `Surgical Tool > CANNULA > Meta > ILM Distance` — distance from tip to ILM surface (mm); `3.4e38` = sentinel / lost
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
