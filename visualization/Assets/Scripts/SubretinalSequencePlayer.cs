using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Loads the "Subretinal Injection 1" dataset frame by frame. Each time-step
// folder under "iOCT Microscope/Volume/" has 513 B-scan PNGs that make up one
// 3D OCT volume - per frame we build a Texture3D from those, load the Canvas
// PNG, and parse the Numerical JSON for the info label.
public class SubretinalSequencePlayer : MonoBehaviour
{
    [Header("Data Root")]
    [Tooltip("Absolute path to the 'Subretinal Injection 1' folder. Leave blank and " +
             "it'll try to auto-detect it next to the project's Assets folder when " +
             "you hit Play (this way the scene still works on other people's machines).")]
    public string DataRootPath;

    private const string DefaultDataFolderName = "Subretinal Injection 1";

    [Header("Volume Rendering")]
    public Material VolumeMaterial;
    [Range(1, 16)]
    [Tooltip("Load every Nth B-scan. 1=all 513 slices (slow), 4=128 slices (recommended)")]
    public int BscanStep = 4;

    [Header("2D Canvas Overlay")]
    public RawImage CanvasDisplay;

    [Header("Top View + Crosshair (real-world anchor)")]
    [Tooltip("Shows Stereo Left/{frame}/microscope.png")]
    public RawImage TopViewDisplay;

    [Header("3D Scene Plane")]
    [Tooltip("Assign a Quad here, it gets auto-positioned below the OCT volume")]
    public Renderer StereoLeftPlane;
    [Tooltip("The OCT_Volume transform used to position the plane")]
    public Transform VolumeTransform;
    [Tooltip("How many times wider than the OCT volume the context plane should be")]
    public float PlaneScale = 5f;

    [Header("Tissue Tension (OCT difference vs. frame 0)")]
    [Tooltip("Drives the tension heatmap by comparing each frame's OCT volume against the " +
             "frame-0 baseline (this is basically the 'compare current scan to initial state' " +
             "idea from the PDF). If left empty it just looks for a TensionHeatmapController " +
             "on this same GameObject.")]
    public TensionHeatmapController TensionController;
    [Tooltip("Compute + upload the tissue-tension heatmap on every frame change")]
    public bool ShowTissueTension = true;
    [Range(1, 8)]
    [Tooltip("How much to downsample the tension volume compared to the OCT volume. " +
             "Bigger number = coarser grid but cheaper to upload each frame.")]
    public int TensionDownsample = 4;
    [Range(0f, 0.5f)]
    [Tooltip("Any intensity change smaller than this (0-1 normalized) is assumed to just be OCT speckle noise, not real tension")]
    public float TensionNoiseFloor = 0.06f;
    [Range(0.5f, 8f)]
    [Tooltip("How sensitive the tension response is once it's above the noise floor")]
    public float TensionGain = 3f;
    [Tooltip("If on, loads the per-frame segmentation masks into the feature volume instead of tension. " +
             "Otherwise the tension heatmap is what gets shown as the overlay.")]
    public bool LoadSegmentationOverlay = false;

    [Header("Flow Advection (NPR tension flow)")]
    [Tooltip("Advects a grain noise texture along the tension gradient so you can actually SEE " +
             "the stress as a moving flow instead of just a static color (this is the idea from " +
             "PDF slide 9). Requires ShowTissueTension to be on too.")]
    public bool EnableFlowAdvection = true;
    [Tooltip("3D grain-noise texture to use. If you leave this empty it'll try to load " +
             "'Resources/Calculated3DTextures/TensionGrainNoise', and if that's not there " +
             "either it just generates one on the fly.")]
    public Texture3D FlowNoiseTexture;
    [Range(1f, 40f)]
    [Tooltip("How many times the noise pattern repeats across the volume - higher number = finer grain")]
    public float FlowNoiseScale = 12f;
    [Range(0f, 4f)]
    [Tooltip("Base speed the flow scrolls at (gets multiplied by the local tension too)")]
    public float FlowSpeed = 1.2f;
    [Range(0f, 2f)]
    [Tooltip("How strong/contrasty the flowing streaks look")]
    public float FlowStrength = 1.0f;
    [Range(16, 96)]
    [Tooltip("Resolution of the noise cube if we have to generate it at runtime (only used when nothing is assigned/found)")]
    public int FlowNoiseResolution = 64;

    [Header("Rendering")]
    [Range(0.001f, 0.05f)]
    [Tooltip("Raymarching step size we push to the material on startup - make this bigger if FPS is bad")]
    public float RenderStepSize = 0.008f;
    [Tooltip("Thin UI Image stretched between OCT Crosshair Start 0 / End 0")]
    public RectTransform CrosshairLine0;
    [Tooltip("Thin UI Image stretched between OCT Crosshair Start 1 / End 1")]
    public RectTransform CrosshairLine1;

    [Header("Playback")]
    public bool AutoLoadOnStart = true;
    public bool AutoPlay        = false;
    [Range(0.5f, 10f)] public float PlayFPS = 3f;

    [Header("Angle Demo Override")]
    [Tooltip("Check this to manually override the injection angle with the slider below to test green/yellow/red color changes.")]
    public bool OverrideAngle = false;
    [Range(0f, 90f)] public float DemoAngleDeg = 45f;


    [Header("UI Bindings")]
    public Slider FrameSlider;
    public TMP_Text FrameLabel;
    public TMP_Text InfoLabel;
    public TMP_Text LoadingLabel;
    public Button PrevButton;
    public Button NextButton;
    public Button PlayPauseButton;

    [Header("Live Sync (Python sonification)")]
    [Tooltip("Auto-found via FindObjectOfType if left empty. Once it has received at " +
             "least one /ioct/state_json message, its frame_index drives playback " +
             "instead of the manual slider/auto-play — Python becomes the clock.")]
    public IOCTOscReceiver OscReceiver;

    // ── Public state ──────────────────────────────────────────────────────────
    public int  TotalFrames  => _frameDirs != null ? _frameDirs.Length : 0;
    public int  CurrentFrame => _currentFrame;
    public bool IsLoading    => _loading;
    public bool IsPlaying    => _playing;
    public bool IsLiveSyncActive => OscReceiver != null && OscReceiver.hasState;

    // ── 3D spatial data (for injection angle sync) ──────────────────────────────
    // the iOCT Microscope transform maps world <-> OCT-local space directly,
    // since the fundus-to-OCT registration is already known for this dataset
    public Vector3    CannulaTipWorld { get; private set; }
    public Vector3    EyeballCenter   { get; private set; }
    public Vector3    IOCTOrigin      { get; private set; }
    public Quaternion IOCTRotation    { get; private set; } = Quaternion.identity;

    // ── 2D keypoint angle (reliable every frame, no trajectory needed) ─────────
    // Parsed directly from JSON "Keypoints" → "Cannula SRI" → Tip + Start.
    // Angle = atan2(|dx|, |dy|) = degrees from vertical (0°=vertical, 90°=horizontal).
    // Returns -1 when the cannula is out of frame (Start is null in the JSON).
    public Vector2 CannulaTip2D   { get; private set; }   // pixels in 2048×2048 image
    public Vector2 CannulaStart2D { get; private set; }   // pixels — entry point at eye surface
    public float   InjectionAngleDeg { get; private set; } = -1f;  // -1 = no reading

    // Maps the world coordinates to the local volume cube [-0.5, 0.5] space.
    public Vector3 CannulaTipLocal { get; private set; }

    // ── Distance to Retina (ILM) in millimeters ─────────────────────────────────
    // Exposed to drive dynamic effects (like sonar pointer speed) in the HUD.
    public float ILMDistanceMM { get; private set; } = -1f;

    // ── Cannula Tip Depth in Millimeters ────────────────────────────────────────
    // Exposes the raw vertical depth of the cannula relative to the scanner top.
    public float CannulaTipDepthMM { get; private set; } = 0f;

    // ── RPE Distance in Millimeters ─────────────────────────────────────────────
    // Distance from the cannula tip to the Retinal Pigment Epithelium (RPE) base layer.
    public float RPEDistanceMM { get; private set; } = -1f;

    // ── Crosshair Center (microscope image pixel space) ─────────────────────────
    // Centroid of the iOCT scan footprint on the 2048×2048 microscope.png.
    // Used to align the 3D volume exactly over the correct spot of the 2D plane.
    public Vector2 OCTCrosshairCenter { get; private set; } = new Vector2(1024f, 1024f);

    [Header("Volume Coordinate Mapping")]
    public float LateralExtentMM = 30f;
    public float DepthExtentMM   = 40f;
    public enum LocalAxis { X, Y, Z }
    public LocalAxis DepthAxis = LocalAxis.Y;
    public LocalAxis ColumnAxis = LocalAxis.X;
    public LocalAxis SliceAxis = LocalAxis.Z;

    // CPU-side copy of the OCT volume so we can sample gradient/normal without a
    // GPU readback. index = z*w*h + y*w + x, same layout as the uploaded Texture3D.
    public byte[] VolumeBytes  { get; private set; }
    public int    VolumeVoxelW { get; private set; }
    public int    VolumeVoxelH { get; private set; }
    public int    VolumeVoxelD { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────
    // frame-0 OCT volume, this is our "PreDeformation" baseline - every later
    // frame gets compared against this one to figure out tissue tension
    private byte[]    _referenceVolume;

    // CPU-side copy of the current frame's segmentation labels, same layout as VolumeBytes.
    // Used to mask the needle out of the tension diff — see LoadSegmentationFeatureVolume.
    private byte[]    _currentSegBytes;

    // Cannula depth from the previous frame, used to tell whether the needle is advancing
    // or retracting so tension smoothing (TensionController.ApplyDirectionalSmoothing) knows
    // which way to move. Null until we've seen one valid depth reading.
    private float?    _lastTensionDepthMM;

    private string[]  _frameDirs;
    private int       _currentFrame;
    private bool      _loading;
    private bool      _playing;
    private float     _playTimer;
    private Texture3D _volumeTex;
    private Texture3D _segTex;
    private Texture3D _generatedNoise; // grain noise we generate ourselves if nothing was assigned/found
    private Texture2D _canvasTex;
    private Texture2D _topViewTex;

    // StereoLeftPlane's offset from VolumeTransform, in the volume's unrotated
    // body frame (see SyncPlaneToVolume)
    private Vector3 _planeLocalOffset;

    private const float TopViewImagePx = 2048f; // Stereo Left microscope.png is always 2048x2048

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        Debug.LogWarning("[SubretinalPlayer] Awake called.");
        // grab the tension controller and turn off its synthetic-demo mode before
        // Start() runs, otherwise it uploads a synthetic frame first
        if (ShowTissueTension)
        {
            if (TensionController == null) TensionController = GetComponent<TensionHeatmapController>();
            if (TensionController == null) TensionController = FindObjectOfType<TensionHeatmapController>();
            if (TensionController != null)
            {
                TensionController.UseSyntheticData = false;
                TensionController.AnimateSynthetic = false;
            }
        }
    }

    void Start()
    {
        Debug.LogWarning($"[SubretinalPlayer] Start called. DataRootPath: '{DataRootPath}'. Directory exists: {Directory.Exists(DataRootPath)}");
        WireButtons();

        if (OscReceiver == null) OscReceiver = FindObjectOfType<IOCTOscReceiver>();

        if (VolumeMaterial != null)
        {
            VolumeMaterial.SetFloat("StepSize", RenderStepSize);
            // 1 substep seems to be enough, saves a bunch of extra texture lookups per step
            VolumeMaterial.SetFloat("FeatureSamplingDensity", 0.25f);
            // the jitter helps avoid banding rings when using bigger step sizes
            VolumeMaterial.EnableKeyword("STOCHASTIC_JITTER");

            SetupFlowAdvection();
        }

        if (StereoLeftPlane == null)
            CreateStereoLeftPlane();

        PositionPlaneUnderVolume();

        TryAutoDetectDataRoot();

        if (!Directory.Exists(DataRootPath))
        {
            SetStatus("Data folder not found.\nSet DataRootPath in Inspector.");
            return;
        }

        BuildFrameList();

        if (AutoLoadOnStart && TotalFrames > 0)
            StartCoroutine(LoadFrame(0));
    }

    // looks for "Subretinal Injection 1" next to the Assets folder, recomputed
    // every Play so the scene doesn't store someone's personal absolute path.
    // If not found, falls back to checking the parent directory for raw directories.
    void TryAutoDetectDataRoot()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string candidate    = Path.Combine(projectRoot, DefaultDataFolderName);
        Debug.LogWarning($"[SubretinalPlayer] TryAutoDetectDataRoot. projectRoot: '{projectRoot}', candidate: '{candidate}', candidate exists: {Directory.Exists(candidate)}");
        if (Directory.Exists(candidate))
        {
            DataRootPath = candidate;
            return;
        }

        // Check parent directory (e.g. "d:\TUM Exchange")
        string parentDir = Path.GetFullPath(Path.Combine(projectRoot, ".."));
        string parentIoct = Path.Combine(parentDir, "iOCT Microscope");
        Debug.LogWarning($"[SubretinalPlayer] Checking parentDir: '{parentDir}', parentIoct: '{parentIoct}', exists: {Directory.Exists(parentIoct)}");
        if (Directory.Exists(parentIoct))
        {
            DataRootPath = parentDir;
            return;
        }
    }

    void Update()
    {
        // has to run every frame (not just when we load a new data frame) so that
        // dragging OCT_Volume around with RotateVolume drags StereoLeftPlane too
        SyncPlaneToVolume();

        // Once Python's sonification clock starts driving us (see LateUpdate), stop
        // advancing on our own so the two frame sources don't fight each other.
        if (!_playing || _loading || TotalFrames == 0 || IsLiveSyncActive) return;
        _playTimer += Time.deltaTime;
        if (_playTimer >= 1f / PlayFPS)
        {
            _playTimer = 0f;
            StartCoroutine(LoadFrame((_currentFrame + 1) % TotalFrames));
        }
    }

    // LateUpdate (not Update) so this always runs after IOCTOscReceiver.Update(),
    // regardless of Unity's per-frame script execution order — hasNewState is only
    // meaningful for the remainder of the frame it was set in.
    void LateUpdate()
    {
        if (OscReceiver == null || !OscReceiver.hasNewState || _loading || TotalFrames == 0) return;

        // Only follow Python's clock if it's driving the same capture we have loaded,
        // so running sonification on a different dataset doesn't yank the viewer along.
        string loadedCaptureName = Path.GetFileName(
            DataRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(OscReceiver.currentCaptureName) &&
            OscReceiver.currentCaptureName != loadedCaptureName)
        {
            return;
        }

        int targetFrame = Mathf.Clamp(OscReceiver.latestFrameIndex, 0, TotalFrames - 1);
        if (targetFrame != _currentFrame)
        {
            GoToFrame(targetFrame);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void GoToFrame(int index)
    {
        if (_loading) return;
        StartCoroutine(LoadFrame(Mathf.Clamp(index, 0, TotalFrames - 1)));
    }

    public void NextFrame() => GoToFrame((_currentFrame + 1) % TotalFrames);
    public void PrevFrame() => GoToFrame((_currentFrame - 1 + TotalFrames) % TotalFrames);

    public void TogglePlay()
    {
        _playing   = !_playing;
        _playTimer = 0f;
        var lbl = PlayPauseButton != null ? PlayPauseButton.GetComponentInChildren<TMP_Text>() : null;
        if (lbl != null) lbl.text = _playing ? "⏸" : "▶";
    }

    // ── Frame loading (coroutine) ─────────────────────────────────────────────

    void BuildFrameList()
    {
        string volRoot = Path.Combine(DataRootPath, "iOCT Microscope", "Volume");
        if (!Directory.Exists(volRoot))
        {
            Debug.LogError($"[SubretinalPlayer] Volume folder not found: {volRoot}");
            return;
        }

        _frameDirs = Directory.GetDirectories(volRoot)
            .OrderBy(Path.GetFileName)
            .ToArray();

        Debug.Log($"[SubretinalPlayer] {TotalFrames} time frames found.");

        if (FrameSlider != null)
        {
            FrameSlider.minValue     = 0;
            FrameSlider.maxValue     = TotalFrames - 1;
            FrameSlider.wholeNumbers = true;
            FrameSlider.onValueChanged.RemoveAllListeners();
            FrameSlider.onValueChanged.AddListener(v => GoToFrame((int)v));
        }
    }

    IEnumerator LoadFrame(int index)
    {
        _loading      = true;
        _currentFrame = index;

        string frameDir  = _frameDirs[index];
        string frameName = Path.GetFileName(frameDir);

        SetStatus($"Loading {frameName}...");

        // ── 1. Assemble OCT Texture3D ────────────────────────────────────
        int w, h, depth;
        byte[] volBytes;

        if (OctRawCache.TryRead(DataRootPath, frameName, BscanStep, out volBytes, out w, out h, out depth))
        {
            // cache hit, so we can skip decoding the PNGs again and just reuse the bytes
            SetStatus($"Loading {frameName}... (cached)");
            yield return null;
        }
        else
        {
            string[] selected = OctRawCache.SelectBscans(frameDir, BscanStep);
            depth = selected.Length;
            if (depth == 0) { _loading = false; yield break; }

            // just read the first slice to figure out width/height
            Texture2D probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            probe.LoadImage(File.ReadAllBytes(selected[0]));
            w = probe.width; h = probe.height;
            Destroy(probe);

            volBytes         = new byte[w * h * depth];
            int yieldEvery   = Mathf.Max(1, depth / 16);

            for (int i = 0; i < depth; i++)
            {
                Texture2D slice = new Texture2D(w, h, TextureFormat.RGBA32, false);
                slice.LoadImage(File.ReadAllBytes(selected[i]));
                Color32[] px = slice.GetPixels32();
                int offset   = i * w * h;
                for (int p = 0; p < px.Length; p++)
                    volBytes[offset + p] = px[p].r;
                Destroy(slice);

                if (i % yieldEvery == 0)
                {
                    SetStatus($"Loading {frameName}... {100 * i / depth}%");
                    yield return null;
                }
            }

            // save it so next time we hit this frame we don't have to decode again
            OctRawCache.Write(DataRootPath, frameName, BscanStep, volBytes, w, h, depth);
        }

        if (_volumeTex != null) Destroy(_volumeTex);
        _volumeTex            = new Texture3D(w, h, depth, TextureFormat.R8, false);
        _volumeTex.wrapMode   = TextureWrapMode.Clamp;
        _volumeTex.filterMode = FilterMode.Bilinear;
        _volumeTex.SetPixelData(volBytes, 0);
        _volumeTex.Apply();

        VolumeBytes  = volBytes;
        VolumeVoxelW = w;
        VolumeVoxelH = h;
        VolumeVoxelD = depth;

        if (VolumeMaterial != null)
        {
            VolumeMaterial.SetTexture("VolumeTex",          _volumeTex);
            VolumeMaterial.SetFloat  ("BlinnPhongTextureX", w);
            VolumeMaterial.SetFloat  ("BlinnPhongTextureY", h);
            VolumeMaterial.SetFloat  ("BlinnPhongTextureZ", depth);
        }

        // ── 1a. Numerical JSON — read early so CannulaTipDepthMM reflects THIS frame
        // before the tension step below needs it to judge advancing vs retracting.
        string jsonPath = Path.Combine(DataRootPath, "Numerical", frameName + ".json");
        string json     = File.Exists(jsonPath) ? File.ReadAllText(jsonPath) : null;
        if (json != null) ParseSpatialFromJson(json);

        // ── 1b. Advance/retract direction, from this frame's depth vs. the last one we saw.
        // Feeds TensionController.ApplyDirectionalSmoothing below.
        // Default false (decay), not true: an invalid/lost depth reading (CannulaTipDepthMM
        // <= 0, same signal InjectionRing3D uses to gray out the ring) must NOT be read as
        // "still advancing" — that would hold tension up indefinitely whenever tracking is
        // lost, which is the opposite of what "diminish when retracting" is supposed to do.
        bool advancing = false;
        if (_lastTensionDepthMM.HasValue && CannulaTipDepthMM > 0f)
            advancing = CannulaTipDepthMM >= _lastTensionDepthMM.Value;
        if (CannulaTipDepthMM > 0f) _lastTensionDepthMM = CannulaTipDepthMM;

        // ── 1c. Segmentation → CPU bytes (tension needle mask) + optional Feature Volume.
        // Loaded before tension now, since tension needs _currentSegBytes this frame.
        // Tension and the visual overlay share the same feature-volume GPU slot, so the
        // overlay only uploads to GPU when tension isn't already using it for display
        // (LoadSegmentationOverlay forces it either way) — the CPU-side mask always loads.
        bool tensionOwnsFeature = ShowTissueTension && TensionController != null;
        string segDir = Path.Combine(frameDir, "Segmentation");
        bool showSegOverlay = LoadSegmentationOverlay || !tensionOwnsFeature;
        if (Directory.Exists(segDir) && (tensionOwnsFeature || showSegOverlay))
            yield return StartCoroutine(LoadSegmentationFeatureVolume(segDir, w, h, depth, showSegOverlay));

        // ── 1d. Tissue tension → Feature Volume ──────────────────────────
        // compares this frame against the frame-0 baseline, intensity change drives the heatmap
        if (tensionOwnsFeature)
        {
            if (index == 0 || _referenceVolume == null || _referenceVolume.Length != volBytes.Length)
                _referenceVolume = (byte[])volBytes.Clone();

            TensionController.SetTensionFromOCTDifference(
                volBytes, _referenceVolume, w, h, depth,
                TensionDownsample, TensionNoiseFloor, TensionGain,
                _currentSegBytes);

            TensionController.ApplyDirectionalSmoothing(advancing);
        }

        // ── 2. Canvas PNG ─────────────────────────────────────────────────
        string canvasPath = Path.Combine(DataRootPath, "Canvas", frameName + ".png");
        if (File.Exists(canvasPath) && CanvasDisplay != null)
        {
            if (_canvasTex == null) _canvasTex = new Texture2D(2, 2);
            _canvasTex.LoadImage(File.ReadAllBytes(canvasPath));
            CanvasDisplay.texture = _canvasTex;
        }

        // ── 3. Top view (real-world anchor) ────────────────────────────────
        string topViewPath = Path.Combine(DataRootPath, "Stereo Left", frameName, "microscope.png");
        if (File.Exists(topViewPath))
        {
            if (_topViewTex == null) _topViewTex = new Texture2D(2, 2);
            _topViewTex.LoadImage(File.ReadAllBytes(topViewPath));

            if (TopViewDisplay != null)
                TopViewDisplay.texture = _topViewTex;

            // also put the same image on the 3D scene plane if we have one assigned
            if (StereoLeftPlane != null)
                StereoLeftPlane.sharedMaterial.mainTexture = _topViewTex;
        }

        // ── 4. Numerical JSON display (json already read + parsed in step 1a) ──────
        string info = $"Frame {frameName}  |  {depth} slices (step={BscanStep})\n";
        if (json != null) info += ParseInfoFromJson(json);
        if (InfoLabel != null) InfoLabel.text = info;

        if (json != null)
        {
            PositionCrosshair(json);
            AlignStereoLeftPlaneToCrosshair(json, _topViewTex);
        }

        // ── 5. UI sync ────────────────────────────────────────────────────
        if (FrameSlider != null) FrameSlider.SetValueWithoutNotify(index);
        if (FrameLabel  != null) FrameLabel.text = IsLiveSyncActive
            ? $"Frame {index:000} / {TotalFrames - 1:000}  [LIVE]"
            : $"Frame {index:000} / {TotalFrames - 1:000}";
        SetStatus("");

        _loading = false;
    }

    // ── Flow advection (NPR tension flow) ────────────────────────────────────

    // sets grain-noise + flow params and enables FLOW_ADVECTION. tries the
    // Inspector-assigned noise, then a shared Resources asset, then generates
    // one at runtime as a fallback
    void SetupFlowAdvection()
    {
        if (!EnableFlowAdvection || !ShowTissueTension)
        {
            VolumeMaterial.DisableKeyword("FLOW_ADVECTION");
            return;
        }

        Texture3D noise = FlowNoiseTexture;
        if (noise == null)
            noise = Resources.Load<Texture3D>("Calculated3DTextures/TensionGrainNoise");
        if (noise == null)
        {
            _generatedNoise = GenerateGrainNoise(Mathf.Clamp(FlowNoiseResolution, 16, 128));
            noise = _generatedNoise;
        }

        VolumeMaterial.SetTexture("FlowNoiseTex",   noise);
        VolumeMaterial.SetFloat  ("FlowNoiseScale", FlowNoiseScale);
        VolumeMaterial.SetFloat  ("FlowSpeed",      FlowSpeed);
        VolumeMaterial.SetFloat  ("FlowStrength",   FlowStrength);
        VolumeMaterial.EnableKeyword("FLOW_ADVECTION");
    }

    // tileable value-noise cube for the flow-advection grain, same idea as the
    // editor NoiseVolumeGenerator but fewer octaves so it's fast at startup
    static Texture3D GenerateGrainNoise(int res)
    {
        var tex = new Texture3D(res, res, res, TextureFormat.R8, false)
        {
            wrapMode   = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        byte[]    data = new byte[res * res * res];
        var       rng  = new System.Random(12345); // fixed seed so it looks the same every run
        Vector3[] off  = new Vector3[3];
        for (int o = 0; o < off.Length; o++)
            off[o] = new Vector3((float)rng.NextDouble() * 1000f,
                                 (float)rng.NextDouble() * 1000f,
                                 (float)rng.NextDouble() * 1000f);

        int idx = 0;
        for (int z = 0; z < res; z++)
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float v = 0f, amp = 1f, freq = 6f, maxAmp = 0f;
            for (int o = 0; o < off.Length; o++)
            {
                float nx = x / (float)res * freq + off[o].x;
                float ny = y / (float)res * freq + off[o].y;
                float nz = z / (float)res * freq + off[o].z;
                float s  = (Mathf.PerlinNoise(nx, ny) + Mathf.PerlinNoise(ny, nz) + Mathf.PerlinNoise(nx, nz)) / 3f;
                v += s * amp; maxAmp += amp; amp *= 0.5f; freq *= 2f;
            }
            data[idx++] = (byte)(Mathf.Clamp01(v / maxAmp) * 255f + 0.5f);
        }

        tex.SetPixelData(data, 0);
        tex.Apply();
        return tex;
    }

    // ── 3D plane creation + positioning ──────────────────────────────────────

    void CreateStereoLeftPlane()
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "StereoLeftPlane";

        Material mat = new Material(Shader.Find("Unlit/Texture"));
        quad.GetComponent<Renderer>().material = mat;
        Destroy(quad.GetComponent<MeshCollider>());

        StereoLeftPlane = quad.GetComponent<Renderer>();
    }

    void PositionPlaneUnderVolume()
    {
        if (StereoLeftPlane == null) return;

        if (VolumeTransform != null)
        {
            // offset in the volume's unrotated body frame, see SyncPlaneToVolume() below
            float halfH = VolumeTransform.lossyScale.y * 0.5f;
            _planeLocalOffset = new Vector3(0f, -(halfH + 0.02f), 0f);

            // not parenting to the volume - it has a non-uniform scale that would shear the quad
            float sx = VolumeTransform.lossyScale.x * PlaneScale;
            float sz = VolumeTransform.lossyScale.z * PlaneScale;
            StereoLeftPlane.transform.localScale = new Vector3(sx, sz, 1f);

            SyncPlaneToVolume();
        }
        else
        {
            // fallback if VolumeTransform isn't assigned
            _planeLocalOffset = new Vector3(0f, -0.7f, 0f);
            StereoLeftPlane.transform.position   = _planeLocalOffset;
            StereoLeftPlane.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
            StereoLeftPlane.transform.localScale = new Vector3(5f, 5f, 1f);
        }
    }

    // keeps StereoLeftPlane glued under OCT_Volume - recomputes world position/
    // rotation from the volume's current rotation every call. can't just parent
    // it in Unity since OCT_Volume's non-uniform scale would shear the quad.
    void SyncPlaneToVolume()
    {
        if (StereoLeftPlane == null || VolumeTransform == null) return;

        Quaternion rot = VolumeTransform.rotation;
        StereoLeftPlane.transform.position = VolumeTransform.position + rot * _planeLocalOffset;
        StereoLeftPlane.transform.rotation = rot * Quaternion.Euler(90f, 0f, 0f); // lay it flat, then apply the volume's rotation on top
    }

    // ── Segmentation Feature Volume ───────────────────────────────────────────

    IEnumerator LoadSegmentationFeatureVolume(string segDir, int w, int h, int depth, bool uploadToGpu = true)
    {
        // Stale from a previous frame otherwise — if this frame has no segmentation, the
        // tension mask (and any GPU overlay) should reflect that, not silently reuse old data.
        _currentSegBytes = null;

        // only care about the numbered B-scan slices, skip stuff like cannula.png
        string[] segFiles = Directory.GetFiles(segDir, "*.png")
            .Where(f => Regex.IsMatch(Path.GetFileNameWithoutExtension(f), @"^\d+$"))
            .OrderBy(Path.GetFileNameWithoutExtension)
            .Where((_, i) => i % BscanStep == 0)
            .Take(depth)
            .ToArray();

        if (segFiles.Length == 0) yield break;

        int    segDepth   = segFiles.Length;
        byte[] segBytes   = new byte[w * h * segDepth];
        int    yieldEvery = Mathf.Max(1, segDepth / 8);

        for (int i = 0; i < segDepth; i++)
        {
            Texture2D slice = new Texture2D(w, h, TextureFormat.RGBA32, false);
            slice.LoadImage(File.ReadAllBytes(segFiles[i]));
            Color32[] px = slice.GetPixels32();
            int offset = i * w * h;
            for (int p = 0; p < px.Length; p++)
                segBytes[offset + p] = px[p].r;
            Destroy(slice);
            if (i % yieldEvery == 0) yield return null;
        }

        // CPU-side copy, kept around so tension masking can use it even when the GPU
        // overlay (below) isn't being shown.
        _currentSegBytes = segBytes;

        if (!uploadToGpu) yield break;

        if (_segTex != null) Destroy(_segTex);
        _segTex            = new Texture3D(w, h, segDepth, TextureFormat.R8, false);
        _segTex.wrapMode   = TextureWrapMode.Clamp;
        _segTex.filterMode = FilterMode.Bilinear;
        _segTex.SetPixelData(segBytes, 0);
        _segTex.Apply();

        if (VolumeMaterial != null)
        {
            VolumeMaterial.SetTexture("FeatureVolumeTex", _segTex);
            VolumeMaterial.SetFloat("FeatureTexResX", w);
            VolumeMaterial.SetFloat("FeatureTexResY", h);
            VolumeMaterial.SetFloat("FeatureTexResZ", segDepth);
            VolumeMaterial.EnableKeyword("SHOW_FEATURE_VOLUME");
            VolumeMaterial.SetFloat("SHOW_FEATURE_VOLUME", 1f);
        }

        Debug.Log($"[SubretinalPlayer] Segmentation Feature Volume loaded ({w}x{h}x{segDepth})");
    }

    // ── Crosshair overlay (real-world anchor on the top view) ────────────────

    // stretches the two crosshair line RectTransforms to match "OCT Crosshair"
    // (2048x2048 microscope.png pixel space) so the thumbnail shows the slice
    void PositionCrosshair(string json)
    {
        if (CrosshairLine0 == null && CrosshairLine1 == null) return;
        if (TopViewDisplay == null) return;

        if (!TryGetCrosshair(json, out Vector2 s0, out Vector2 e0, out Vector2 s1, out Vector2 e1))
            return;

        Rect rect = TopViewDisplay.rectTransform.rect;
        if (CrosshairLine0 != null) PositionLine(CrosshairLine0, s0, e0, rect);
        if (CrosshairLine1 != null) PositionLine(CrosshairLine1, s1, e1, rect);
    }

    // reads "OCT Crosshair" Start/End 0 and 1 (the two scan arms), false if missing
    static bool TryGetCrosshair(string json, out Vector2 s0, out Vector2 e0, out Vector2 s1, out Vector2 e1)
    {
        s0 = e0 = s1 = e1 = Vector2.zero;

        int chIdx = json.IndexOf("\"OCT Crosshair\"", StringComparison.Ordinal);
        if (chIdx < 0) return false;

        Vector2? ps0 = GetNamedPoint(json, "Start 0", chIdx);
        Vector2? pe0 = GetNamedPoint(json, "End 0",   chIdx);
        Vector2? ps1 = GetNamedPoint(json, "Start 1", chIdx);
        Vector2? pe1 = GetNamedPoint(json, "End 1",   chIdx);
        if (!ps0.HasValue || !pe0.HasValue || !ps1.HasValue || !pe1.HasValue) return false;

        s0 = ps0.Value; e0 = pe0.Value; s1 = ps1.Value; e1 = pe1.Value;
        return true;
    }

    // ── 3D plane alignment (real-world anchor via OCT Crosshair) ─────────────

    // aligns StereoLeftPlane so the 30x30mm square the OCT Crosshair marks on
    // microscope.png lines up under OCT_Volume's own footprint - replaces the
    // old fixed PlaneScale guess, which assumed the crosshair was centered (it isn't)
    void AlignStereoLeftPlaneToCrosshair(string json, Texture2D topViewTex)
    {
        if (StereoLeftPlane == null || VolumeTransform == null || topViewTex == null)
            return;

        if (!TryGetCrosshair(json, out Vector2 s0, out Vector2 e0, out Vector2 s1, out Vector2 e1))
        {
            PositionPlaneUnderVolume(); // fall back to the old centered guess
            return;
        }

        Vector2 scanCenterPx = (s0 + e0 + s1 + e1) * 0.25f;
        float   scanSidePx   = ((e0 - s0).magnitude + (e1 - s1).magnitude) * 0.5f;
        if (scanSidePx < 1f)
        {
            PositionPlaneUnderVolume();
            return;
        }

        // VolumeTransform's X footprint is already the volume's real
        // LateralExtentMM (see BaseVisualizationSetup - localScale.x == 1 unit
        // == 30mm), so this stays correct even if the volume gets rescaled later
        float unitsPerPx = VolumeTransform.lossyScale.x / scanSidePx;

        float imgW = topViewTex.width;
        float imgH = topViewTex.height;

        // quad local Y maps to world +Z after the 90deg flat-lay rotation, and the
        // UVs put local Y=+0.5 at V=1 (image top), so the Y offset sign flips for
        // world Z while X carries straight through (same y-flip as PositionLine)
        float dxPx = scanCenterPx.x - imgW * 0.5f;
        float dyPx = scanCenterPx.y - imgH * 0.5f;
        float offsetX = -dxPx * unitsPerPx;
        float offsetZ =  dyPx * unitsPerPx;

        float halfH = VolumeTransform.lossyScale.y * 0.5f;
        _planeLocalOffset = new Vector3(offsetX, -(halfH + 0.02f), offsetZ); // height still eyeballed

        StereoLeftPlane.transform.localScale = new Vector3(imgW * unitsPerPx, imgH * unitsPerPx, 1f);
        SyncPlaneToVolume();
    }

    static Vector2? GetNamedPoint(string json, string key, int fromIndex)
    {
        int idx = json.IndexOf($"\"{key}\"", fromIndex, StringComparison.Ordinal);
        if (idx < 0) return null;
        var v = GetArrayFloats(json, idx);
        if (!float.TryParse(v[0], out float x) || !float.TryParse(v[1], out float y)) return null;
        return new Vector2(x, y);
    }

    // maps a Start->End pixel-space line onto a RectTransform, matches the RawImage's y-flip
    static void PositionLine(RectTransform line, Vector2 pStart, Vector2 pEnd, Rect rect)
    {
        Vector2 uvStart = new Vector2(pStart.x / TopViewImagePx, 1f - pStart.y / TopViewImagePx);
        Vector2 uvEnd   = new Vector2(pEnd.x   / TopViewImagePx, 1f - pEnd.y   / TopViewImagePx);

        Vector2 a = new Vector2((uvStart.x - 0.5f) * rect.width, (uvStart.y - 0.5f) * rect.height);
        Vector2 b = new Vector2((uvEnd.x   - 0.5f) * rect.width, (uvEnd.y   - 0.5f) * rect.height);

        Vector2 mid   = (a + b) * 0.5f;
        Vector2 delta = b - a;
        float   len   = delta.magnitude;
        float   angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        line.anchoredPosition = mid;
        line.sizeDelta = new Vector2(len, line.sizeDelta.y);
        line.localRotation = Quaternion.Euler(0, 0, angle);
    }
    // reads the CANNULA tip, Eyeball translation, and iOCT Microscope transform
    // (world<->OCT-local mapping, used to get the needle tip into voxel space)
    void ParseSpatialFromJson(string json)
    {
        // Reset cannula properties to default/lost state before parsing
        CannulaTipWorld   = Vector3.zero;
        CannulaTipLocal   = Vector3.zero;
        CannulaTip2D      = Vector2.zero;
        CannulaStart2D    = Vector2.zero;
        InjectionAngleDeg = -1f;
        ILMDistanceMM     = float.MaxValue;
        RPEDistanceMM     = float.MaxValue;
        CannulaTipDepthMM = 0f;

        int surgTool = json.IndexOf("\"Surgical Tool\"", StringComparison.Ordinal);
        if (surgTool >= 0)
        {
            int cannula = json.IndexOf("\"CANNULA\"", surgTool, StringComparison.Ordinal);
            if (cannula >= 0)
            {
                int spatial = json.IndexOf("\"Spatial\"", cannula, StringComparison.Ordinal);
                if (spatial >= 0)
                {
                    int tipIdx = json.IndexOf("\"Tip\"", spatial, StringComparison.Ordinal);
                    if (tipIdx >= 0) CannulaTipWorld = GetVector3At(json, tipIdx);

                    // Sentinel check: tracker outputs Y≈1000 when needle is retracted/lost.
                    // Treat this the same as Vector3.zero so the ring stays hidden.
                    if (CannulaTipWorld.y > 500f)
                        CannulaTipWorld = Vector3.zero;
                }
            }
        }

        int eyeball = json.IndexOf("\"Eyeball\"", StringComparison.Ordinal);
        if (eyeball >= 0)
        {
            int spatial = json.IndexOf("\"Spatial\"", eyeball, StringComparison.Ordinal);
            if (spatial >= 0)
            {
                int transIdx = json.IndexOf("\"Translation\"", spatial, StringComparison.Ordinal);
                if (transIdx >= 0) EyeballCenter = GetVector3At(json, transIdx);
            }
        }

        int ioct = json.IndexOf("\"iOCT Microscope\"", StringComparison.Ordinal);
        if (ioct >= 0)
        {
            int spatial = json.IndexOf("\"Spatial\"", ioct, StringComparison.Ordinal);
            if (spatial >= 0)
            {
                int transIdx = json.IndexOf("\"Translation\"", spatial, StringComparison.Ordinal);
                if (transIdx >= 0) IOCTOrigin = GetVector3At(json, transIdx);

                int rotIdx = json.IndexOf("\"Rotation\"", spatial, StringComparison.Ordinal);
                if (rotIdx >= 0) IOCTRotation = GetQuaternionAt(json, rotIdx);
            }
        }

        // ── 2D Cannula SRI keypoints → injection angle ────────────────────
        // "Cannula SRI" → "Tip"   = needle tip position in 2048×2048 image px
        // "Cannula SRI" → "Start" = entry point at eye surface in image px
        // The vector Start→Tip = needle direction in 2D image space.
        // Angle from vertical = atan2(|dx|, |dy|) in degrees.
        int sriIdx = json.IndexOf("\"Cannula SRI\"", StringComparison.Ordinal);
        if (sriIdx >= 0)
        {
            int tipIdx   = json.IndexOf("\"Tip\"",   sriIdx, StringComparison.Ordinal);
            int startIdx = json.IndexOf("\"Start\"", sriIdx, StringComparison.Ordinal);

            if (tipIdx >= 0)
            {
                float[] tv = GetArrayFloatsN(json, tipIdx, 2);
                CannulaTip2D = new Vector2(tv[0], tv[1]);
            }

            if (startIdx >= 0)
            {
                float[] sv = GetArrayFloatsN(json, startIdx, 2);
                CannulaStart2D = new Vector2(sv[0], sv[1]);

                // Compute angle only when both points are valid
                if (tipIdx >= 0)
                {
                    // Check if coordinates are within valid image boundaries [0, 2048]
                    // If they are far outside, the tracker has lost the needle
                    bool tipValid   = (CannulaTip2D.x >= 0f && CannulaTip2D.x <= 2048f && CannulaTip2D.y >= 0f && CannulaTip2D.y <= 2048f);
                    bool startValid = (CannulaStart2D.x >= 0f && CannulaStart2D.x <= 2048f && CannulaStart2D.y >= 0f && CannulaStart2D.y <= 2048f);

                    if (tipValid && startValid)
                    {
                        float dx = CannulaTip2D.x - CannulaStart2D.x;
                        float dy = CannulaTip2D.y - CannulaStart2D.y;
                        // atan2(|dx|, |dy|) = angle from vertical axis (0° = straight down)
                        InjectionAngleDeg = Mathf.Atan2(Mathf.Abs(dx), Mathf.Abs(dy)) * Mathf.Rad2Deg;
                    }
                    else
                    {
                        // Tracker lost / out of image boundaries
                        InjectionAngleDeg = -1f;
                        CannulaTipWorld   = Vector3.zero;
                    }
                }
            }
            else
            {
                // Start is null → cannula out of frame
                InjectionAngleDeg = -1f;
            }
        }

        // ── Parse OCT Crosshair Center (for 3D/2D alignment) ──────────────
        int chIdx = json.IndexOf("\"OCT Crosshair\"", StringComparison.Ordinal);
        if (chIdx >= 0)
        {
            Vector2? s0 = GetNamedPoint(json, "Start 0", chIdx);
            Vector2? e0 = GetNamedPoint(json, "End 0",   chIdx);
            if (s0.HasValue && e0.HasValue)
            {
                OCTCrosshairCenter = (s0.Value + e0.Value) * 0.5f;
            }
        }

        // ── Parse ILM Distance as float ──────────────────────────────────
        string ilmStr = GetFloat(json, "ILM Distance");
        if (float.TryParse(ilmStr, out float val) && !float.IsNaN(val) && !float.IsInfinity(val))
        {
            // Safeguard: If distance is float.MaxValue sentinel (3.4e38), treat as float.MaxValue
            ILMDistanceMM = val > 100f ? float.MaxValue : val;
        }
        else
        {
            ILMDistanceMM = float.MaxValue;
        }

        // ── Parse RPE Distance as float ──────────────────────────────────
        string rpeStr = GetFloat(json, "RPE Distance");
        if (float.TryParse(rpeStr, out float rpeVal) && !float.IsNaN(rpeVal) && !float.IsInfinity(rpeVal))
        {
            // Safeguard: If distance is float.MaxValue sentinel, treat as float.MaxValue
            RPEDistanceMM = rpeVal > 100f ? float.MaxValue : rpeVal;
        }
        else
        {
            RPEDistanceMM = float.MaxValue;
        }

        // Apply manual demo override if active
        if (OverrideAngle)
        {
            InjectionAngleDeg = DemoAngleDeg;
        }

        // Compute local tip coordinates mapped to volume cube
        ComputeLocalTipPosition();
    }

    static Vector3 GetVector3At(string json, int fromIndex)
    {
        float[] v = GetArrayFloatsN(json, fromIndex, 3);
        return new Vector3(v[0], v[1], v[2]);
    }

    static Quaternion GetQuaternionAt(string json, int fromIndex)
    {
        float[] v = GetArrayFloatsN(json, fromIndex, 4);
        return new Quaternion(v[0], v[1], v[2], v[3]);
    }

    static float[] GetArrayFloatsN(string json, int fromIndex, int n)
    {
        float[] result = new float[n];
        int bracket = json.IndexOf('[', fromIndex);
        if (bracket < 0) return result;
        int end = json.IndexOf(']', bracket);
        if (end < 0) return result;
        string inner = json.Substring(bracket + 1, end - bracket - 1);
        var nums = Regex.Matches(inner, @"[\d.eE+\-]+");
        for (int i = 0; i < n && i < nums.Count; i++)
            float.TryParse(nums[i].Value, out result[i]);
        return result;
    }

    // ── JSON parser (didn't want to pull in an external dependency for this) ──

    static string ParseInfoFromJson(string json)
    {
        string ilm    = GetFloat(json, "ILM Distance");
        string rpe    = GetFloat(json, "RPE Distance");

        // Cannula SRI keypoints: "Cannula SRI" -> just grab the first array after it
        string tipX = "—", tipY = "—", startX = "—", startY = "—";
        int sri = json.IndexOf("\"Cannula SRI\"", StringComparison.Ordinal);
        if (sri >= 0)
        {
            int tipIdx   = json.IndexOf("\"Tip\"",   sri, StringComparison.Ordinal);
            int startIdx = json.IndexOf("\"Start\"", sri, StringComparison.Ordinal);
            if (tipIdx   >= 0) { var v = GetArrayFloats(json, tipIdx);   tipX   = v[0]; tipY   = v[1]; }
            if (startIdx >= 0) { var v = GetArrayFloats(json, startIdx); startX = v[0]; startY = v[1]; }
        }

        return $"ILM dist : {ilm} mm  |  RPE dist: {rpe} mm\n" +
               $"Tip   px : ({tipX}, {tipY})\n" +
               $"Start px : ({startX}, {startY})";
    }

    static string GetFloat(string json, string key)
    {
        var m = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*([\\d.eE+\\-]+)");
        return m.Success ? m.Groups[1].Value : "—";
    }

    static string[] GetArrayFloats(string json, int fromIndex)
    {
        int bracket = json.IndexOf('[', fromIndex);
        if (bracket < 0) return new[] { "—", "—" };
        int end     = json.IndexOf(']', bracket);
        if (end < 0)     return new[] { "—", "—" };
        string inner = json.Substring(bracket + 1, end - bracket - 1);
        var nums     = Regex.Matches(inner, @"[\d.eE+\-]+");
        string x     = nums.Count > 0 ? nums[0].Value : "—";
        string y     = nums.Count > 1 ? nums[1].Value : "—";
        return new[] { x, y };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void WireButtons()
    {
        if (PrevButton      != null) PrevButton.onClick.AddListener(PrevFrame);
        if (NextButton      != null) NextButton.onClick.AddListener(NextFrame);
        if (PlayPauseButton != null) PlayPauseButton.onClick.AddListener(TogglePlay);
    }

    void ComputeLocalTipPosition()
    {
        if (CannulaTipWorld == Vector3.zero)
        {
            CannulaTipLocal = Vector3.zero;
            return;
        }

        // Safeguard: Ensure IOCTRotation is a valid normalized quaternion
        Quaternion rot = IOCTRotation;
        float magSq = rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w;
        if (Mathf.Abs(magSq - 1.0f) > 0.1f)
        {
            rot = Quaternion.identity;
        }

        // Map world coordinate to iOCT-local millimeters
        Vector3 localMM = Quaternion.Inverse(rot) * (CannulaTipWorld - IOCTOrigin);

        float depthVal = -Component(localMM, DepthAxis); // depth increases as we go lower (more negative Y)
        float colVal   = Component(localMM, ColumnAxis);
        float sliceVal = Component(localMM, SliceAxis);

        CannulaTipDepthMM = depthVal;

        // Prevent division by zero if dimensions are 0 in Inspector
        float latExtent = LateralExtentMM > 0.01f ? LateralExtentMM : 30f;
        float depExtent = DepthExtentMM > 0.01f ? DepthExtentMM : 40f;

        // Normalize to [0, 1] range based on physical volume dimensions
        float u = Mathf.Clamp01((colVal   + latExtent * 0.5f) / latExtent);
        float v = Mathf.Clamp01(depthVal  / depExtent);
        float s = Mathf.Clamp01((sliceVal + latExtent * 0.5f) / latExtent);

        // Map to [-0.5, 0.5] local bounds of the volume cube
        // Invert Y: v = 0 (top of volume/scanner) -> localY = 0.5, v = 1 (bottom of volume/RPE) -> localY = -0.5
        CannulaTipLocal = new Vector3(u - 0.5f, 0.5f - v, s - 0.5f);
    }

    static float Component(Vector3 v, LocalAxis axis)
    {
        if (axis == LocalAxis.X) return v.x;
        if (axis == LocalAxis.Y) return v.y;
        return v.z;
    }

    void SetStatus(string msg) { if (LoadingLabel != null) LoadingLabel.text = msg; }

    void OnDestroy()
    {
        if (_volumeTex      != null) Destroy(_volumeTex);
        if (_segTex         != null) Destroy(_segTex);
        if (_canvasTex      != null) Destroy(_canvasTex);
        if (_topViewTex     != null) Destroy(_topViewTex);
        if (_generatedNoise != null) Destroy(_generatedNoise);
    }
}
