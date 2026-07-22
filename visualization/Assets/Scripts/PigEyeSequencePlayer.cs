using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Plays back a pig-eye OCT sequence frame by frame and drives
/// TensionHeatmapController with real force-sensor data.
///
/// Setup in the scene:
///   1. Attach this component to any GameObject.
///   2. Assign TensionHeatmapController reference.
///   3. Assign BscanRenderer (MeshRenderer on a plane/quad) for B-scan display.
///   4. Optionally assign SegRenderer for segmentation overlay.
///   5. Set SequenceFolder to the full path of the pig-eye sequence
///      (e.g. ".../Data/pigeye_samples/no_bounceback/full_seg/06_28_23/b_i3").
///   6. Press Play — the sequence loads automatically if AutoLoadOnStart is true.
/// </summary>
public class PigEyeSequencePlayer : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────

    [Header("Data")]
    [Tooltip("Absolute or project-relative path to the sequence folder (e.g. b_i3)")]
    public string SequenceFolder;

    [Tooltip("Load and go to frame 0 automatically on Start")]
    public bool AutoLoadOnStart = true;

    [Header("Force Mapping")]
    [Tooltip("TipForceNorm_mN value that maps to tension = 1.0 (fully red)")]
    public float MaxForceMN = 10f;

    [Tooltip("Gaussian sigma of the heatmap blob, in UV space [0,1]")]
    [Range(0.02f, 0.3f)]
    public float HeatmapSigma = 0.08f;

    [Header("Playback")]
    [Tooltip("Play forward automatically")]
    public bool AutoPlay = false;

    [Tooltip("Frames per second during auto-play")]
    [Range(1f, 60f)]
    public float PlaybackFPS = 10f;

    [Tooltip("Loop back to frame 0 when the sequence ends")]
    public bool Loop = true;

    [Header("Rendering — Composite Overlay (recommended)")]
    [Tooltip("Material using TensionViz/TensionOverlay2D shader on a Quad.\n" +
             "Drives _BscanTex, _TensionTex, _NeedleTipUV, _HighTensionPulse automatically.")]
    public Material OverlayMaterial;

    [Header("Rendering — Separate (fallback / debug)")]
    [Tooltip("MeshRenderer whose main texture is set to the raw B-scan (no heatmap)")]
    public Renderer BscanRenderer;

    [Tooltip("MeshRenderer whose main texture is set to the segmentation mask (optional)")]
    public Renderer SegRenderer;

    [Header("Controllers")]
    public TensionHeatmapController  HeatmapController;
    public InjectionAngleController  AngleController;   // optional — assign for angle demo scene

    // ─── Public read-only state ───────────────────────────────────────────

    public int              TotalFrames  => _frames != null ? _frames.Count : 0;
    public int              CurrentIndex => _currentIndex;
    public PigEyeFrame      CurrentFrame => _frames != null && _frames.Count > 0
                                              ? _frames[_currentIndex] : default;
    public PigEyeLabels     Labels       => _labels;
    public DeformationPhase CurrentPhase => PigEyeDataLoader.GetPhase(CurrentFrame.Index, _labels);
    public bool             IsLoaded     => _frames != null && _frames.Count > 0;

    // ─── Private state ────────────────────────────────────────────────────

    private List<PigEyeFrame> _frames;
    private PigEyeLabels      _labels;
    private int               _currentIndex;
    private float             _playbackTimer;

    private Texture2D _bscanTex;
    private Texture2D _segTex;

    // cached B-scan dimensions (read from first loaded frame)
    private int _bscanW = 1000;
    private int _bscanH = 1024;

    [Header("On-Screen HUD")]
    [Tooltip("Show frame / force / phase info overlay in the Game view")]
    public bool ShowHUD = true;

    // ─── Unity lifecycle ──────────────────────────────────────────────────

    void Start()
    {
        if (AutoLoadOnStart && !string.IsNullOrEmpty(SequenceFolder))
            LoadSequence(SequenceFolder);
    }

    void OnGUI()
    {
        if (!ShowHUD || !IsLoaded) return;

        PigEyeFrame f = CurrentFrame;

        // background box
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(8, 8, 280, 96), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize  = 14;
        style.fontStyle = FontStyle.Bold;

        // Phase colour
        Color phaseColor = CurrentPhase switch
        {
            DeformationPhase.PreDeformation  => new Color(0.4f, 1f, 1f),
            DeformationPhase.Deforming       => Color.yellow,
            DeformationPhase.PeakDeformation => new Color(1f, 0.35f, 0.35f),
            DeformationPhase.PostDeformation => Color.gray,
            _                                => Color.white
        };

        float y = 12f;
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(14, y, 270, 20), $"Frame  {f.Index:D3} / {TotalFrames - 1:D3}", style);

        y += 22f;
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(14, y, 270, 20),
            $"Force  {f.TipForceNorm:F3} mN{(f.ValidForce ? "" : "  (invalid)")}", style);

        y += 22f;
        float tension = HeatmapController != null ? HeatmapController.NormalizedTension : 0f;
        style.normal.textColor = Color.Lerp(new Color(0.4f, 1f, 0.4f), new Color(1f, 0.35f, 0.35f), tension);
        GUI.Label(new Rect(14, y, 270, 20), $"Tension  {tension:P0}", style);

        y += 22f;
        style.normal.textColor = phaseColor;
        GUI.Label(new Rect(14, y, 270, 20), CurrentPhase.ToString(), style);
    }

    void Update()
    {
        if (!IsLoaded) return;

        // Arrow-key stepping (editor convenience)
        if (Input.GetKeyDown(KeyCode.RightArrow)) StepForward();
        if (Input.GetKeyDown(KeyCode.LeftArrow))  StepBackward();

        // Auto-play
        if (AutoPlay)
        {
            _playbackTimer += Time.deltaTime;
            if (_playbackTimer >= 1f / Mathf.Max(PlaybackFPS, 0.1f))
            {
                _playbackTimer = 0f;
                if (_currentIndex < _frames.Count - 1)
                    GoToFrame(_currentIndex + 1);
                else if (Loop)
                    GoToFrame(0);
                else
                    AutoPlay = false;
            }
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────

    /// <summary>Loads (or reloads) the sequence from the given folder path.</summary>
    public void LoadSequence(string folder)
    {
        _frames = PigEyeDataLoader.LoadSequence(folder, out _labels);
        _currentIndex = 0;
        _playbackTimer = 0f;
        SequenceFolder = folder;

        if (IsLoaded)
            GoToFrame(0);
    }

    public void StepForward()
    {
        if (!IsLoaded) return;
        GoToFrame(Mathf.Min(_currentIndex + 1, _frames.Count - 1));
    }

    public void StepBackward()
    {
        if (!IsLoaded) return;
        GoToFrame(Mathf.Max(_currentIndex - 1, 0));
    }

    /// <summary>Jumps to frame at list-index (not frame.Index) and updates all visuals.</summary>
    public void GoToFrame(int listIndex)
    {
        if (!IsLoaded) return;
        listIndex = Mathf.Clamp(listIndex, 0, _frames.Count - 1);
        _currentIndex = listIndex;

        PigEyeFrame f = _frames[listIndex];
        LoadBscan(f.BscanPath);
        LoadSeg(f.SegPath);
        UpdateHeatmap(f);           // populates HeatmapController.TensionMap2D
        UpdateOverlayMaterial(f);   // drives TensionOverlay2D shader
        UpdateAngleOverlay(f);      // drives InjectionAngleOverlay shader (if assigned)
    }

    // ─── Internal helpers ─────────────────────────────────────────────────

    private void LoadBscan(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        byte[] data = File.ReadAllBytes(path);
        if (_bscanTex == null)
            _bscanTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        _bscanTex.LoadImage(data);
        _bscanW = _bscanTex.width;
        _bscanH = _bscanTex.height;

        if (BscanRenderer != null)
            BscanRenderer.material.mainTexture = _bscanTex;
    }

    private void LoadSeg(string path)
    {
        // Always load the texture (needed by InjectionAngleController even without SegRenderer)
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            byte[] data = File.ReadAllBytes(path);
            if (_segTex == null) _segTex = new Texture2D(2, 2, TextureFormat.R8, false);
            _segTex.LoadImage(data);
        }

        // Optionally display on SegRenderer
        if (SegRenderer == null) return;
        if (_segTex == null) { SegRenderer.enabled = false; return; }
        SegRenderer.material.mainTexture = _segTex;
        SegRenderer.enabled = true;
    }

    private void UpdateHeatmap(PigEyeFrame f)
    {
        if (HeatmapController == null) return;

        // Force sensor is inactive across all pig eye sequences (ValidTipForce always 0).
        // Use deformation labels as ground-truth tension source instead:
        //   frame < TStart          → tension 0
        //   TStart ≤ frame < TPeak  → linear ramp 0 → 1
        //   frame ≥ TPeak           → tension 1
        float tension = ComputeLabelTension(f.Index);

        HeatmapController.SetForceBasedTension(
            tipForceNorm_mN: tension,
            needleTipX:      f.NeedleTipX,
            needleTipY:      f.NeedleTipY,
            bscanWidth:      _bscanW,
            bscanHeight:     _bscanH,
            maxForceMN:      1f,   // tension already in [0,1]
            sigma:           HeatmapSigma);
    }

    private float ComputeLabelTension(int frameIndex)
    {
        if (_labels == null) return 0f;
        if (frameIndex < _labels.TStart) return 0f;
        if (frameIndex >= _labels.TPeak)  return 1f;
        return (float)(frameIndex - _labels.TStart) / (_labels.TPeak - _labels.TStart);
    }

    // Drives all TensionOverlay2D shader properties on OverlayMaterial.
    private void UpdateOverlayMaterial(PigEyeFrame f)
    {
        if (OverlayMaterial == null) return;

        // B-scan background
        if (_bscanTex != null)
            OverlayMaterial.SetTexture("_BscanTex", _bscanTex);

        // Tension map (populated by UpdateHeatmap via HeatmapController)
        if (HeatmapController != null && HeatmapController.TensionMap2D != null)
            OverlayMaterial.SetTexture("_TensionTex", HeatmapController.TensionMap2D);

        // Needle tip — convert pixel coords to UV, flip Y (image Y=0 is top, UV Y=0 is bottom)
        float u = _bscanW > 0 ? f.NeedleTipX / (float)_bscanW : 0.5f;
        float v = _bscanH > 0 ? 1f - f.NeedleTipY / (float)_bscanH : 0.5f;
        OverlayMaterial.SetVector("_NeedleTipUV", new Vector4(u, v, 0f, 0f));

        // Aspect ratio for circular crosshair
        float aspect = _bscanH > 0 ? (float)_bscanW / _bscanH : 1f;
        OverlayMaterial.SetFloat("_AspectRatio", aspect);

        // High-tension warning pulse
        float pulse = (HeatmapController != null && HeatmapController.HighTensionAlert) ? 1f : 0f;
        OverlayMaterial.SetFloat("_HighTensionPulse", pulse);
    }

    private void UpdateAngleOverlay(PigEyeFrame f)
    {
        if (AngleController == null) return;

        float u = _bscanW > 0 ? f.NeedleTipX / (float)_bscanW       : 0.5f;
        float v = _bscanH > 0 ? 1f - f.NeedleTipY / (float)_bscanH  : 0.5f;

        AngleController.UpdateAngle(
            segTex:      _segTex,
            needleTipUV: new Vector2(u, v),
            bscanW:      _bscanW,
            bscanH:      _bscanH);
    }

    void OnDestroy()
    {
        if (_bscanTex != null) Destroy(_bscanTex);
        if (_segTex   != null) Destroy(_segTex);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Custom Inspector
// ─────────────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(PigEyeSequencePlayer))]
public class PigEyeSequencePlayerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PigEyeSequencePlayer player = (PigEyeSequencePlayer)target;

        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField("Sequence Controls", UnityEditor.EditorStyles.boldLabel);

        if (!player.IsLoaded)
        {
            if (GUILayout.Button("Load Sequence"))
                player.LoadSequence(player.SequenceFolder);
        }
        else
        {
            // Frame slider
            int newIdx = UnityEditor.EditorGUILayout.IntSlider(
                "Frame", player.CurrentIndex, 0, player.TotalFrames - 1);
            if (newIdx != player.CurrentIndex)
                player.GoToFrame(newIdx);

            // Step buttons
            UnityEditor.EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("◀ Prev")) player.StepBackward();
            if (GUILayout.Button("Next ▶")) player.StepForward();
            UnityEditor.EditorGUILayout.EndHorizontal();

            UnityEditor.EditorGUILayout.Space();
            UnityEditor.EditorGUILayout.LabelField("Current Frame Info", UnityEditor.EditorStyles.boldLabel);

            PigEyeFrame f = player.CurrentFrame;
            UnityEditor.EditorGUILayout.LabelField("Frame Index",    f.Index.ToString());
            UnityEditor.EditorGUILayout.LabelField("TipForceNorm",   $"{f.TipForceNorm:F4} mN");
            UnityEditor.EditorGUILayout.LabelField("Needle Tip",     $"({f.NeedleTipX:F1}, {f.NeedleTipY:F1}) px");
            UnityEditor.EditorGUILayout.LabelField("Valid Force",    f.ValidForce.ToString());
            UnityEditor.EditorGUILayout.LabelField("Seg Available",  (f.SegPath != null).ToString());

            // Deformation phase with color
            if (player.Labels != null)
            {
                DeformationPhase phase = player.CurrentPhase;
                Color orig = GUI.color;
                GUI.color = phase switch
                {
                    DeformationPhase.PreDeformation  => Color.cyan,
                    DeformationPhase.Deforming        => Color.yellow,
                    DeformationPhase.PeakDeformation  => Color.red,
                    DeformationPhase.PostDeformation  => Color.gray,
                    _                                 => Color.white
                };
                UnityEditor.EditorGUILayout.LabelField("Deformation Phase", phase.ToString());
                GUI.color = orig;

                UnityEditor.EditorGUILayout.LabelField(
                    "GT Labels",
                    $"t_start={player.Labels.TStart}  t_peak={player.Labels.TPeak}  t_end={player.Labels.TEnd}");
            }

            // Reload button
            UnityEditor.EditorGUILayout.Space();
            if (GUILayout.Button("Reload Sequence"))
                player.LoadSequence(player.SequenceFolder);
        }
    }
}
#endif
