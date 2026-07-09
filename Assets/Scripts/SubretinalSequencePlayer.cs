using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loads the "Subretinal Injection 1" dataset frame by frame.
///
/// Each time-step folder under "iOCT Microscope/Volume/" contains 513 B-scan PNGs
/// that form one 3D OCT volume. Per frame advance this player:
///   1. Reads selected B-scan PNGs → assembles Texture3D → pushes to DVR material
///   2. Loads the matching Canvas PNG → shows on CanvasDisplay RawImage
///   3. Parses the Numerical JSON → shows ILM distance, cannula position in InfoLabel
/// </summary>
public class SubretinalSequencePlayer : MonoBehaviour
{
    [Header("Data Root")]
    [Tooltip("Absolute path to the 'Subretinal Injection 1' folder. Leave blank to " +
             "auto-detect it as a sibling of this project's Assets folder at Play time " +
             "(so the scene stays portable across teammates' machines).")]
    public string DataRootPath;

    private const string DefaultDataFolderName = "Subretinal Injection 1";

    [Header("Volume Rendering")]
    public Material VolumeMaterial;
    [Range(1, 16)]
    [Tooltip("Load every Nth B-scan. 1=all 513 slices (slow), 8=64 slices (fast for ring demo)")]
    public int BscanStep = 8;

    [Header("2D Canvas Overlay")]
    public RawImage CanvasDisplay;

    [Header("Top View + Crosshair (real-world anchor)")]
    [Tooltip("Shows Stereo Left/{frame}/microscope.png")]
    public RawImage TopViewDisplay;
    [Tooltip("Thin UI Image stretched between OCT Crosshair Start 0 / End 0")]
    public RectTransform CrosshairLine0;
    [Tooltip("Thin UI Image stretched between OCT Crosshair Start 1 / End 1")]
    public RectTransform CrosshairLine1;

    [Header("Playback")]
    public bool AutoLoadOnStart = true;
    public bool AutoPlay        = false;
    [Range(0.5f, 10f)] public float PlayFPS = 3f;

    [Header("3D/2D Scene Anchoring")]
    [Tooltip("The root Transform of the 3D DVR volume cube. Its position and rotation will be set each frame from the iOCT JSON data.")]
    public Transform VolumeTransform;

    [Tooltip("A flat Quad/Plane shown below the 3D volume as the 2D enface (top-down) view.")]
    public Transform EnfacePlane;

    [Tooltip("Scale factor: mm world coordinates → Unity units. 0.05 means 1 mm = 0.05 Unity units.")]
    public float MmToUnits = 0.05f;

    [Tooltip("How far below the volume origin (in Unity units) the 2D enface plane sits. Make more negative to move plane lower.")]
    public float PlaneOffsetY = -0.67f;

    [Header("Angle Demo Override")]
    [Tooltip("Check this to manually override the injection angle with the slider below to test green/yellow/red color changes.")]
    public bool OverrideAngle = false;
    [Range(0f, 90f)] public float DemoAngleDeg = 45f;

    [Header("UI Bindings")]
    public Slider FrameSlider;
    public Text   FrameLabel;
    public Text   InfoLabel;
    public Text   LoadingLabel;
    public Button PrevButton;
    public Button NextButton;
    public Button PlayPauseButton;

    // ── Public state ──────────────────────────────────────────────────────────
    public int  TotalFrames  => _frameDirs != null ? _frameDirs.Length : 0;
    public int  CurrentFrame => _currentFrame;
    public bool IsLoading    => _loading;
    public bool IsPlaying    => _playing;

    // ── 3D spatial data (for injection angle sync) ──────────────────────────────
    // World-space cannula tip, eyeball center, and the iOCT Microscope's own
    // transform — the latter is the world<->OCT-local mapping, since the
    // fundus-to-OCT registration for this dataset is known ground truth.
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

    // ── 3D Cannula local position relative to volume center ─────────────────────
    // Maps the world coordinates to the local volume cube [-0.5, 0.5] space.
    public Vector3 CannulaTipLocal { get; private set; }

    // ── Distance to Retina (ILM) in millimeters ─────────────────────────────────
    // Exposed to drive dynamic effects (like sonar pulse speed) in the HUD.
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

    // CPU-side copy of the currently loaded OCT volume (R channel, one byte per
    // voxel), kept around so a gradient/normal can be sampled without a GPU
    // readback. Layout matches the uploaded Texture3D: index = z*w*h + y*w + x.
    public byte[] VolumeBytes  { get; private set; }
    public int    VolumeVoxelW { get; private set; }
    public int    VolumeVoxelH { get; private set; }
    public int    VolumeVoxelD { get; private set; }

    // ── Private ───────────────────────────────────────────────────────────────
    private string[]  _frameDirs;
    private int       _currentFrame;
    private bool      _loading;
    private bool      _playing;
    private float     _playTimer;
    private Texture3D _volumeTex;
    private Texture2D _canvasTex;
    private Texture2D _topViewTex;

    private const float TopViewImagePx = 2048f; // Stereo Left microscope.png is 2048x2048

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Start()
    {
        WireButtons();

        // ── Auto-link missing references ──
        if (VolumeTransform == null)
        {
            var go = GameObject.Find("OCT_Volume");
            if (go != null) VolumeTransform = go.transform;
        }

        if (EnfacePlane == null)
        {
            var go = GameObject.Find("EnfacePlane");
            if (go != null) EnfacePlane = go.transform;
        }

        if (string.IsNullOrEmpty(DataRootPath) || !Directory.Exists(DataRootPath))
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

    // Looks for "Subretinal Injection 1" next to this project's Assets folder.
    // Recomputed fresh on every machine at Play time, so a saved scene doesn't
    // carry one teammate's machine-specific absolute path.
    void TryAutoDetectDataRoot()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string candidate    = Path.Combine(projectRoot, DefaultDataFolderName);
        if (Directory.Exists(candidate))
        {
            DataRootPath = candidate;
            return;
        }

        // Auto-detect fallback paths commonly used on your machine
        string[] fallbacks = {
            @"D:\TUM Exchange",
            @"d:\TUM Exchange",
            @"C:\TUM Exchange",
            @"c:\TUM Exchange"
        };
        foreach (var path in fallbacks)
        {
            if (Directory.Exists(path) && Directory.Exists(Path.Combine(path, "iOCT Microscope", "Volume")))
            {
                DataRootPath = path;
                return;
            }
        }
    }

    void Update()
    {
        if (!_playing || _loading || TotalFrames == 0) return;
        _playTimer += Time.deltaTime;
        if (_playTimer >= 1f / PlayFPS)
        {
            _playTimer = 0f;
            StartCoroutine(LoadFrame((_currentFrame + 1) % TotalFrames));
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
        var lbl = PlayPauseButton != null ? PlayPauseButton.GetComponentInChildren<Text>() : null;
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
        string[] allBscans = Directory.GetFiles(frameDir, "*.png")
            .OrderBy(Path.GetFileNameWithoutExtension)
            .ToArray();

        string[] selected = allBscans
            .Where((_, i) => i % BscanStep == 0)
            .ToArray();

        int depth = selected.Length;
        if (depth == 0) { _loading = false; yield break; }

        // probe first slice for dimensions
        Texture2D probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        probe.LoadImage(File.ReadAllBytes(selected[0]));
        int w = probe.width, h = probe.height;
        Destroy(probe);

        byte[] volBytes     = new byte[w * h * depth];
        int    yieldEvery   = Mathf.Max(1, depth / 8);   // yield more often → UI stays responsive

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
        if (File.Exists(topViewPath) && TopViewDisplay != null)
        {
            if (_topViewTex == null) _topViewTex = new Texture2D(2, 2);
            _topViewTex.LoadImage(File.ReadAllBytes(topViewPath));
            TopViewDisplay.texture = _topViewTex;

            // Apply texture to 3D enface plane
            if (EnfacePlane != null)
            {
                var mr = EnfacePlane.GetComponent<MeshRenderer>();
                if (mr != null && mr.material != null)
                {
                    mr.material.mainTexture = _topViewTex;
                }
            }
        }

        // ── 4. Numerical JSON ─────────────────────────────────────────────
        string jsonPath = Path.Combine(DataRootPath, "Numerical", frameName + ".json");
        string info     = $"Frame {frameName}  |  {depth} slices (step={BscanStep})\n";
        string json     = null;
        if (File.Exists(jsonPath))
        {
            json = File.ReadAllText(jsonPath);
            info += ParseInfoFromJson(json);
        }
        if (InfoLabel != null) InfoLabel.text = info;

        if (json != null)
        {
            PositionCrosshair(json);
            ParseSpatialFromJson(json);
            ApplySceneAnchors();   // sync 3D volume + 2D plane to iOCT world position
        }

        // ── 5. UI sync ────────────────────────────────────────────────────
        if (FrameSlider != null) FrameSlider.SetValueWithoutNotify(index);
        if (FrameLabel  != null) FrameLabel.text = $"Frame {index:000} / {TotalFrames - 1:000}";
        SetStatus("");

        _loading = false;
    }

    // ── 3D/2D Scene Anchoring ─────────────────────────────────────────────────

    /// <summary>
    /// Position the 2D enface plane directly below the 3D volume cube,
    /// keeping the 3D volume stationary in front of the camera.
    /// </summary>
    void ApplySceneAnchors()
    {
        if (VolumeTransform != null && EnfacePlane != null)
        {
            // Parent the EnfacePlane to the VolumeTransform so it rotates and moves with it
            if (EnfacePlane.parent != VolumeTransform)
            {
                EnfacePlane.SetParent(VolumeTransform, false);
                EnfacePlane.localScale = new Vector3(1.5f, 1.5f, 1f); // Scale plane so crosshair matches volume
            }

            // Convert crosshair center pixel coordinate [0, 2048] to local offset on the plane
            // The plane local bounds are [-0.5, 0.5]
            float u = OCTCrosshairCenter.x / TopViewImagePx;
            float v = 1f - (OCTCrosshairCenter.y / TopViewImagePx); // Invert Y coordinate for Unity space

            // Shift the plane so that the crosshair footprint is centered exactly under the volume cube
            float localX = (u - 0.5f);
            float localZ = (v - 0.5f);

            EnfacePlane.localPosition = new Vector3(-localX, PlaneOffsetY, -localZ);
            EnfacePlane.localRotation = Quaternion.Euler(90f, 0f, 0f);  // flat relative to volume
        }
    }

    // ── Crosshair overlay (real-world anchor on the top view) ────────────────

    /// <summary>
    /// Reads "OCT Crosshair" (Start 0/1, End 0/1, in 2048x2048 microscope.png
    /// pixel space) and stretches the two assigned line RectTransforms to match,
    /// so the top-view thumbnail shows which slice the OCT volume corresponds to.
    /// </summary>
    void PositionCrosshair(string json)
    {
        if (CrosshairLine0 == null && CrosshairLine1 == null) return;
        if (TopViewDisplay == null) return;

        int chIdx = json.IndexOf("\"OCT Crosshair\"", StringComparison.Ordinal);
        if (chIdx < 0) return;

        Vector2? s0 = GetNamedPoint(json, "Start 0", chIdx);
        Vector2? e0 = GetNamedPoint(json, "End 0",   chIdx);
        Vector2? s1 = GetNamedPoint(json, "Start 1", chIdx);
        Vector2? e1 = GetNamedPoint(json, "End 1",   chIdx);

        Rect rect = TopViewDisplay.rectTransform.rect;
        if (CrosshairLine0 != null && s0.HasValue && e0.HasValue)
            PositionLine(CrosshairLine0, s0.Value, e0.Value, rect);
        if (CrosshairLine1 != null && s1.HasValue && e1.HasValue)
            PositionLine(CrosshairLine1, s1.Value, e1.Value, rect);
    }

    static Vector2? GetNamedPoint(string json, string key, int fromIndex)
    {
        int idx = json.IndexOf($"\"{key}\"", fromIndex, StringComparison.Ordinal);
        if (idx < 0) return null;
        var v = GetArrayFloats(json, idx);
        if (!float.TryParse(v[0], out float x) || !float.TryParse(v[1], out float y)) return null;
        // Safeguard: Ensure points are finite to prevent NaN anchoredPosition errors in UI elements
        if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) return null;
        return new Vector2(x, y);
    }

    // Maps a Start→End pixel-space line (2048x2048 image) onto a UI RectTransform
    // stretched between the two points, matching the RawImage's own y-flip.
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

    // ── 3D spatial parsing (for injection angle sync) ────────────────────────

    /// <summary>
    /// Reads the 3D world-space CANNULA tip, Eyeball translation, and the
    /// iOCT Microscope's own Translation/Rotation — the latter is the
    /// world&lt;-&gt;OCT-local transform used to map the needle tip into the
    /// loaded volume's voxel space.
    ///
    /// Also reads the 2D "Cannula SRI" keypoints (Tip + Start in 2048×2048 pixel
    /// space) and computes InjectionAngleDeg = atan2(|dx|,|dy|) — angle from
    /// vertical. This works reliably on every single frame without needing
    /// multi-frame trajectory tracking.
    /// </summary>
    void ParseSpatialFromJson(string json)
    {
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
                    float dx = CannulaTip2D.x - CannulaStart2D.x;
                    float dy = CannulaTip2D.y - CannulaStart2D.y;
                    // atan2(|dx|, |dy|) = angle from vertical axis (0° = straight down)
                    InjectionAngleDeg = Mathf.Atan2(Mathf.Abs(dx), Mathf.Abs(dy)) * Mathf.Rad2Deg;
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
            // Safeguard: If distance is float.MaxValue sentinel (3.4e38), treat as inside retina (-1f)
            ILMDistanceMM = val > 100f ? -1f : val;
        }
        else
        {
            ILMDistanceMM = -1f;
        }

        // ── Parse RPE Distance as float ──────────────────────────────────
        string rpeStr = GetFloat(json, "RPE Distance");
        if (float.TryParse(rpeStr, out float rpeVal) && !float.IsNaN(rpeVal) && !float.IsInfinity(rpeVal))
        {
            // Safeguard: If distance is float.MaxValue sentinel, treat as inside retina (-1f)
            RPEDistanceMM = rpeVal > 100f ? -1f : rpeVal;
        }
        else
        {
            RPEDistanceMM = -1f;
        }

        // Apply manual demo override if active
        if (OverrideAngle)
        {
            InjectionAngleDeg = DemoAngleDeg;
        }

        // Compute local tip coordinates mapped to volume cube
        ComputeLocalTipPosition();
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

    // ── JSON parser (no external dependency) ─────────────────────────────────

    static string ParseInfoFromJson(string json)
    {
        string ilm    = GetFloat(json, "ILM Distance");
        string rpe    = GetFloat(json, "RPE Distance");

        // Cannula SRI keypoints: "Cannula SRI" → first array that follows
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

    void SetStatus(string msg) { if (LoadingLabel != null) LoadingLabel.text = msg; }

    void OnDestroy()
    {
        if (_volumeTex  != null) Destroy(_volumeTex);
        if (_canvasTex  != null) Destroy(_canvasTex);
        if (_topViewTex != null) Destroy(_topViewTex);
    }
}
