using System;
using UnityEngine;

/// <summary>
/// Tissue Tension Heatmap Controller
///
/// Takes a 3D displacement field (one Vector3 per voxel) and converts it to
/// a tension magnitude volume that is uploaded to the volume render material
/// as FeatureVolumeTex.  The DVR_Minimal shader then overlays the heatmap
/// (blue → green → yellow → red) on top of the OCT volume.
///
/// Interface with other teams:
///   IN  – SetDisplacementField(Vector3[] field, int w, int h, int d)
///           called by the registration team once per frame / time-step
///   OUT – NormalizedTension   (float [0,1]) – current mean tension, read by sonification team
///   OUT – HighTensionAlert    (bool)         – true when mean tension > TensionThreshold
/// </summary>
public class TensionHeatmapController : MonoBehaviour
{
    // ------------------------------------------------------------------ //
    //  Inspector                                                          //
    // ------------------------------------------------------------------ //

    [Header("Material")]
    [Tooltip("Material using DVR_Minimal.shader (must have SHOW_FEATURE_VOLUME support)")]
    public Material volumeMaterial;

    [Header("Tension Parameters")]
    [Tooltip("Displacement magnitude (in voxels) that maps to tension = 1.0")]
    public float MaxDisplacementMagnitude = 10f;

    [Tooltip("Mean normalised tension above which HighTensionAlert is raised")]
    [Range(0f, 1f)]
    public float TensionThreshold = 0.6f;

    [Header("Demo / Test Mode")]
    [Tooltip("Generate synthetic tension data so the shader can be tested before the registration team delivers real data")]
    public bool UseSyntheticData = true;

    [Tooltip("Resolution of the synthetic tension volume")]
    public Vector3Int SyntheticResolution = new Vector3Int(64, 64, 64);

    [Tooltip("Animate the synthetic tension blob over time")]
    public bool AnimateSynthetic = true;

    // ------------------------------------------------------------------ //
    //  Public outputs (read by other teams)                               //
    // ------------------------------------------------------------------ //

    /// <summary>Mean normalised tension for the current frame, [0, 1].</summary>
    public float NormalizedTension { get; private set; }

    /// <summary>True when NormalizedTension exceeds TensionThreshold.</summary>
    public bool HighTensionAlert { get; private set; }

    // ------------------------------------------------------------------ //
    //  Private state                                                      //
    // ------------------------------------------------------------------ //

    private Texture3D _tensionTex;
    private Texture2D _tensionMap2D;  // flat 2D copy for TensionOverlay2D shader
    private float[]   _tensionData;   // scratch buffer, reused across frames

    private int _volW, _volH, _volD;

    /// <summary>
    /// 2D tension texture (R channel = tension [0,1]).
    /// Updated whenever SetForceBasedTension() or SetDisplacementField() is called.
    /// Feed this into TensionOverlay2D shader's _TensionTex property.
    /// </summary>
    public Texture2D TensionMap2D => _tensionMap2D;

    // ------------------------------------------------------------------ //
    //  Unity lifecycle                                                    //
    // ------------------------------------------------------------------ //

    void Start()
    {
        if (volumeMaterial == null && UseSyntheticData)
        {
            Debug.LogWarning("[TensionHeatmap] No material assigned — synthetic mode needs a volume material.");
            return;
        }

        if (UseSyntheticData)
        {
            _volW = SyntheticResolution.x;
            _volH = SyntheticResolution.y;
            _volD = SyntheticResolution.z;
            _tensionData = new float[_volW * _volH * _volD];
            InitTensionTexture(_volW, _volH, _volD);
            UploadSyntheticTension(0f);
        }
    }

    void Update()
    {
        if (volumeMaterial == null) return;

        if (UseSyntheticData && AnimateSynthetic)
        {
            UploadSyntheticTension(Time.time);
        }
    }

    // ------------------------------------------------------------------ //
    //  Public API – called by the Registration team                       //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Pig-eye data path: creates a Gaussian tension blob at the given
    /// needle-tip pixel position with amplitude from the force sensor.
    ///
    /// Call this from PigEyeSequencePlayer every frame instead of
    /// SetDisplacementField().
    /// </summary>
    /// <param name="tipForceNorm_mN">Raw TipForceNorm_mN from robot log.</param>
    /// <param name="needleTipX">Needle-tip x in B-scan pixel coords.</param>
    /// <param name="needleTipY">Needle-tip y in B-scan pixel coords.</param>
    /// <param name="bscanWidth">B-scan image width in pixels (e.g. 1000).</param>
    /// <param name="bscanHeight">B-scan image height in pixels (e.g. 1024).</param>
    /// <param name="maxForceMN">Force value (mN) that maps to tension = 1.</param>
    /// <param name="sigma">Gaussian width in UV space [0,1].</param>
    /// <param name="texResolution">Resolution of the generated tension texture.</param>
    public void SetForceBasedTension(
        float tipForceNorm_mN,
        float needleTipX,
        float needleTipY,
        int   bscanWidth,
        int   bscanHeight,
        float maxForceMN    = 10f,
        float sigma         = 0.08f,
        int   texResolution = 128)
    {
        float peakTension = PigEyeDataLoader.NormalizeForce(tipForceNorm_mN, maxForceMN);
        float u = bscanWidth  > 0 ? needleTipX / bscanWidth        : 0.5f;
        // Unity flips PNG on load (y=0 becomes bottom), so flip v to match displayed image
        float v = bscanHeight > 0 ? 1f - needleTipY / bscanHeight  : 0.5f;

        int res = Mathf.Max(texResolution, 16);
        EnsureScratchBuffer(res, res, 1);

        float sig2 = 2f * sigma * sigma;

        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            float fx = x / (float)(res - 1);
            float fy = y / (float)(res - 1);
            float du = fx - u;
            float dv = fy - v;
            float t  = peakTension * Mathf.Exp(-(du * du + dv * dv) / sig2);
            _tensionData[y * res + x] = Mathf.Clamp01(t);
        }

        // Peak tension (= force at needle tip) is the meaningful scalar for UI + sonification.
        // Spatial mean is near-zero due to the Gaussian being small relative to the texture.
        NormalizedTension = peakTension;
        HighTensionAlert  = NormalizedTension > TensionThreshold;

        if (_volW != res || _volH != res || _volD != 1 || _tensionTex == null)
            InitTensionTexture(res, res, 1);

        UploadTensionTextureFromData();
        ConfigureMaterial();
    }

    /// <summary>
    /// Feed a new displacement field.  Computes per-voxel tension magnitude,
    /// normalises and uploads to GPU.
    ///
    /// field layout: field[z * h * w + y * w + x] = displacement vector (in voxels)
    /// </summary>
    public void SetDisplacementField(Vector3[] field, int w, int h, int d)
    {
        if (field == null || field.Length != w * h * d)
        {
            Debug.LogError("[TensionHeatmap] Displacement field size mismatch.");
            return;
        }

        EnsureScratchBuffer(w, h, d);

        float maxMag = Mathf.Max(MaxDisplacementMagnitude, 1e-6f);
        float sumTension = 0f;

        for (int i = 0; i < field.Length; i++)
        {
            float mag = field[i].magnitude;
            float t   = Mathf.Clamp01(mag / maxMag);
            _tensionData[i] = t;
            sumTension += t;
        }

        NormalizedTension = sumTension / field.Length;
        HighTensionAlert  = NormalizedTension > TensionThreshold;

        if (_volW != w || _volH != h || _volD != d || _tensionTex == null)
        {
            InitTensionTexture(w, h, d);
        }

        UploadTensionTexture();
        ConfigureMaterial();
    }

    // ------------------------------------------------------------------ //
    //  Internal helpers                                                   //
    // ------------------------------------------------------------------ //

    private void EnsureScratchBuffer(int w, int h, int d)
    {
        int needed = w * h * d;
        if (_tensionData == null || _tensionData.Length != needed)
        {
            _tensionData = new float[needed];
            _volW = w; _volH = h; _volD = d;
        }
    }

    private void InitTensionTexture(int w, int h, int d)
    {
        if (_tensionTex != null) Destroy(_tensionTex);

        _tensionTex = new Texture3D(w, h, d, TextureFormat.RFloat, false);
        _tensionTex.wrapMode   = TextureWrapMode.Clamp;
        _tensionTex.filterMode = FilterMode.Bilinear;
        _volW = w; _volH = h; _volD = d;
    }

    private void UploadTensionTexture()
    {
        Color[] colors = new Color[_tensionData.Length];
        float sumTension = 0f;
        for (int i = 0; i < _tensionData.Length; i++)
        {
            float t = _tensionData[i];
            colors[i] = new Color(t, 0f, 0f, 1f);
            sumTension += t;
        }

        NormalizedTension = sumTension / _tensionData.Length;
        HighTensionAlert  = NormalizedTension > TensionThreshold;

        _tensionTex.SetPixels(colors);
        _tensionTex.Apply();

        ConfigureMaterial();
    }

    private void ConfigureMaterial()
    {
        if (volumeMaterial == null) return;   // pig eye / 2D-overlay mode: no DVR material needed
        volumeMaterial.EnableKeyword("SHOW_FEATURE_VOLUME");
        volumeMaterial.SetTexture("FeatureVolumeTex", _tensionTex);
        volumeMaterial.SetFloat("FeatureTexResX", _volW);
        volumeMaterial.SetFloat("FeatureTexResY", _volH);
        volumeMaterial.SetFloat("FeatureTexResZ", _volD);
        // Heatmap spans the full [0,1] range
        volumeMaterial.SetFloat("FeatureBandLow",  0f);
        volumeMaterial.SetFloat("FeatureBandHigh", 1f);
        // Colour LUT: blue→cyan→green→yellow→red (matches CLAUDE.md spec)
        volumeMaterial.SetColor("FeatureLowColor",     new Color(0.00f, 0.20f, 1.00f, 1f)); // blue
        volumeMaterial.SetColor("FeatureMidLowColor",  new Color(0.00f, 0.90f, 1.00f, 1f)); // cyan
        volumeMaterial.SetColor("FeatureMidColor",     new Color(0.20f, 1.00f, 0.25f, 1f)); // green
        volumeMaterial.SetColor("FeatureMidHighColor", new Color(1.00f, 0.95f, 0.15f, 1f)); // yellow
        volumeMaterial.SetColor("FeatureHighColor",    new Color(1.00f, 0.15f, 0.00f, 1f)); // red
        volumeMaterial.SetFloat("FeatureAlphaMultiplier",     50f);
        volumeMaterial.SetFloat("FeatureLowAlphaMultiplier",  0.05f);
        volumeMaterial.SetFloat("FeatureHighAlphaMultiplier", 1.0f);
        volumeMaterial.SetFloat("FeatureAlphaCurve",          2.0f);
    }

    /// <summary>
    /// Generates a Gaussian tension blob that moves across the volume over
    /// time.  Used for visual testing before real registration data arrives.
    /// </summary>
    private void UploadSyntheticTension(float t)
    {
        float cycle    = Mathf.Repeat(t * 0.3f, 1f);
        float blobX    = 0.2f + cycle * 0.6f;
        float blobY    = 0.5f;
        float blobZ    = 0.5f;
        float sigma    = 0.12f;
        float peakMag  = 0.85f + Mathf.Sin(t * 1.2f) * 0.15f; // pulse between 0.7 and 1.0

        // second weaker blob for visual variety
        float blobX2 = 0.8f - cycle * 0.5f;

        float sumTension = 0f;
        int idx = 0;
        for (int z = 0; z < _volD; z++)
        for (int y = 0; y < _volH; y++)
        for (int x = 0; x < _volW; x++)
        {
            float fx = x / (float)(_volW - 1);
            float fy = y / (float)(_volH - 1);
            float fz = z / (float)(_volD - 1);

            float d1 = Mathf.Sqrt((fx - blobX) * (fx - blobX) + (fy - blobY) * (fy - blobY) + (fz - blobZ) * (fz - blobZ));
            float d2 = Mathf.Sqrt((fx - blobX2) * (fx - blobX2) + (fy - 0.4f) * (fy - 0.4f) + (fz - 0.5f) * (fz - 0.5f));

            float tension = peakMag * Mathf.Exp(-(d1 * d1) / (2f * sigma * sigma))
                          + 0.4f   * Mathf.Exp(-(d2 * d2) / (2f * sigma * sigma * 1.5f));

            _tensionData[idx] = Mathf.Clamp01(tension);
            sumTension += _tensionData[idx];
            idx++;
        }

        NormalizedTension = sumTension / _tensionData.Length;
        HighTensionAlert  = NormalizedTension > TensionThreshold;

        UploadTensionTextureFromData();
        ConfigureMaterial();
    }

    private void UploadTensionTextureFromData()
    {
        Color[] colors = new Color[_tensionData.Length];
        for (int i = 0; i < _tensionData.Length; i++)
            colors[i] = new Color(_tensionData[i], 0f, 0f, 1f);

        _tensionTex.SetPixels(colors);
        _tensionTex.Apply();

        SyncTensionMap2D(colors);
    }

    // Keeps a Texture2D mirror of the XY plane (depth=0 slice) for the 2D overlay shader.
    private void SyncTensionMap2D(Color[] colors)
    {
        int w = _volW, h = _volH;
        if (_tensionMap2D == null || _tensionMap2D.width != w || _tensionMap2D.height != h)
        {
            if (_tensionMap2D != null) Destroy(_tensionMap2D);
            _tensionMap2D = new Texture2D(w, h, TextureFormat.RFloat, false);
            _tensionMap2D.wrapMode   = TextureWrapMode.Clamp;
            _tensionMap2D.filterMode = FilterMode.Bilinear;
        }

        // For depth=1 volumes (pig eye) colors IS the 2D slice.
        // For deeper volumes take the first z=0 slice (w*h pixels starting at index 0).
        Color[] slice = new Color[w * h];
        System.Array.Copy(colors, 0, slice, 0, Mathf.Min(w * h, colors.Length));
        _tensionMap2D.SetPixels(slice);
        _tensionMap2D.Apply();
    }

    void OnDestroy()
    {
        if (_tensionTex    != null) Destroy(_tensionTex);
        if (_tensionMap2D  != null) Destroy(_tensionMap2D);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying && volumeMaterial != null && UseSyntheticData)
            ConfigureMaterial();
    }
#endif
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(TensionHeatmapController))]
public class TensionHeatmapControllerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TensionHeatmapController ctrl = (TensionHeatmapController)target;

        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField("Runtime State", UnityEditor.EditorStyles.boldLabel);
        UnityEditor.EditorGUILayout.FloatField("Normalised Tension", ctrl.NormalizedTension);
        UnityEditor.EditorGUILayout.Toggle("High Tension Alert", ctrl.HighTensionAlert);

        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.HelpBox(
            "SetDisplacementField(Vector3[] field, int w, int h, int d)\n" +
            "Call this every frame from the Registration team's script.\n\n" +
            "Read NormalizedTension + HighTensionAlert from this component\n" +
            "to feed the Sonification team.",
            UnityEditor.MessageType.Info);
    }
}
#endif
