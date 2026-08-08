using System;
using UnityEngine;


public class TensionHeatmapController : MonoBehaviour
{
    //  Inspector 

    [Header("Material")]
    [Tooltip("Material using DVR_Minimal.shader (must have SHOW_FEATURE_VOLUME support)")]
    public Material volumeMaterial;

    [Header("Tension Parameters")]
    [Tooltip("Displacement magnitude (in voxels) that maps to tension = 1.0")]
    public float MaxDisplacementMagnitude = 10f;

    [Tooltip("Mean normalised tension above which HighTensionAlert is raised")]
    [Range(0f, 1f)]
    public float TensionThreshold = 0.45f;

    [Tooltip("Percentile of the per-block tension distribution used as NormalizedTension in " +
             "SetTensionFromOCTDifference (0.99 = 99th percentile). We added this because a " +
             "single hot voxel (like a cannula reflection) was triggering \"high tension\" by " +
             "itself, which isn't what we want - now it needs a big enough cluster of blocks.")]
    [Range(0.9f, 1f)]
    public float TensionPercentile = 0.99f;

    [Header("Directional Smoothing")]
    [Tooltip("Byte value in the segmentation volume that marks needle/cannula pixels — " +
             "these are excluded from the tension diff so the needle's own OCT reflection " +
             "doesn't get counted as tissue tension.")]
    public byte NeedleSegLabel = 1;

    [Tooltip("How quickly displayed tension rises toward the raw reading while the needle " +
             "is advancing (higher = snappier).")]
    [Range(0.05f, 1f)]
    public float TensionRiseRate = 0.6f;

    [Tooltip("How quickly displayed tension decays toward zero while the needle is " +
             "retracting or stationary (higher = faster decay).")]
    [Range(0.02f, 1f)]
    public float TensionDecayRate = 0.15f;

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

    // mean normalized tension for the current frame, [0, 1]
    public float NormalizedTension { get; private set; }

    // true when NormalizedTension goes over TensionThreshold
    public bool HighTensionAlert { get; private set; }

    // ------------------------------------------------------------------ //
    //  Private state                                                      //
    // ------------------------------------------------------------------ //

    private Texture3D _tensionTex;
    private Texture2D _tensionMap2D;  // flat 2D copy for TensionOverlay2D shader
    private float[]   _tensionData;   // scratch buffer, reused across frames
    private byte[]    _tensionBytesR8; // scratch buffer for the OCT-difference path (R8 upload)
    private readonly int[] _tensionHist = new int[256]; // reused histogram for the percentile scan

    private int _volW, _volH, _volD;

    // Directionally-smoothed value actually shown on the gauge — distinct from the raw,
    // instantaneous NormalizedTension computed each frame. See ApplyDirectionalSmoothing.
    private float _smoothedTension = 0f;

    // 2D tension texture, updated by SetForceBasedTension()/SetDisplacementField().
    // feed into TensionOverlay2D shader's _TensionTex
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
        // Unity flips the PNG on load, so flip v too or it won't line up
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

        // peak tension is the meaningful scalar here, spatial mean would be near 0
        // since the Gaussian blob is small relative to the whole texture
        NormalizedTension = peakTension;
        HighTensionAlert  = NormalizedTension > TensionThreshold;

        if (_volW != res || _volH != res || _volD != 1 || _tensionTex == null)
            InitTensionTexture(res, res, 1);

        UploadTensionTextureFromData();
        ConfigureMaterial();
    }

    // feed in a new displacement field, computes per-voxel tension, uploads to GPU
    // field layout: field[z * h * w + y * w + x] = displacement vector (in voxels)
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


    // current      = this frame's OCT volume (1 byte per voxel, R channel)
    // reference    = frame-0 baseline volume, same size/layout as current
    // w, h, d      = source volume dimensions in voxels
    // downsample   = box-average factor, tension volume ends up w/n x h/n x d/n
    // noiseFloor   = intensity change below this (0-1 normalized) is just speckle, ignore it
    // gain         = sensitivity multiplier applied after the noise floor cut
    // segmentation = optional, same size/layout as current. Voxels labeled NeedleSegLabel
    //                are excluded from the diff — otherwise the needle's own bright OCT
    //                reflection reads as "tension" regardless of whether it's actually
    //                compressing tissue, since this metric has no other way to tell a
    //                needle apart from displaced tissue.
    public void SetTensionFromOCTDifference(
        byte[] current, byte[] reference,
        int w, int h, int d,
        int    downsample   = 4,
        float  noiseFloor   = 0.06f,
        float  gain         = 3f,
        byte[] segmentation = null)
    {
        if (current == null || reference == null) return;
        if (current.Length != w * h * d || reference.Length != w * h * d)
        {
            Debug.LogError("[TensionHeatmap] OCT volume size mismatch.");
            return;
        }
        if (segmentation != null && segmentation.Length != w * h * d)
        {
            Debug.LogWarning("[TensionHeatmap] Segmentation size mismatch — ignoring needle mask this frame.");
            segmentation = null;
        }

        downsample = Mathf.Clamp(downsample, 1, 16);
        int tw = Mathf.Max(1, w / downsample);
        int th = Mathf.Max(1, h / downsample);
        int td = Mathf.Max(1, d / downsample);

        if (_tensionBytesR8 == null || _tensionBytesR8.Length != tw * th * td)
            _tensionBytesR8 = new byte[tw * th * td];

        float invRange = 1f / Mathf.Max(1e-4f, 1f - noiseFloor);

        int di = 0;
        for (int z = 0; z < td; z++)
        for (int y = 0; y < th; y++)
        for (int x = 0; x < tw; x++)
        {
            // box-average the block so a few random speckle pixels don't read as tension
            int sx0 = x * downsample, sy0 = y * downsample, sz0 = z * downsample;
            int sx1 = Mathf.Min(sx0 + downsample, w);
            int sy1 = Mathf.Min(sy0 + downsample, h);
            int sz1 = Mathf.Min(sz0 + downsample, d);

            int acc = 0, cnt = 0;
            for (int sz = sz0; sz < sz1; sz++)
            {
                int zoff = sz * w * h;
                for (int sy = sy0; sy < sy1; sy++)
                {
                    int row = zoff + sy * w;
                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        int voxel = row + sx;
                        if (segmentation != null && segmentation[voxel] == NeedleSegLabel)
                            continue; // needle pixel — skip, this block should measure tissue only

                        int diff = current[voxel] - reference[voxel];
                        acc += diff < 0 ? -diff : diff;
                        cnt++;
                    }
                }
            }

            // cnt == 0 means this block is entirely needle — no tissue to measure, so no tension
            float meanDiff = cnt > 0 ? (acc / (float)cnt) / 255f : 0f; // [0,1]
            float t = Mathf.Clamp01((meanDiff - noiseFloor) * invRange * gain);
            _tensionBytesR8[di++] = (byte)(t * 255f + 0.5f);
        }


        NormalizedTension = PercentileOf(_tensionBytesR8, TensionPercentile);
        HighTensionAlert  = NormalizedTension > TensionThreshold;

        UploadTensionR8(tw, th, td);
        ConfigureMaterial();
    }

    // Blends the just-computed raw NormalizedTension into a persistent, directionally-aware
    // displayed value: rises toward the raw reading while advancing (real compression
    // building), decays toward zero while retracting/stationary (mirrors tissue relaxing as
    // the needle backs off) rather than tracking the raw per-frame diff directly, which can
    // still read as non-zero from residual noise or imperfect tissue recovery even once the
    // needle mask above removes its direct contribution. Call this right after
    // SetTensionFromOCTDifference()/SetDisplacementField()/SetForceBasedTension() each frame.
    public void ApplyDirectionalSmoothing(bool advancing)
    {
        float raw    = NormalizedTension;
        float target = advancing ? raw : 0f;
        float rate   = advancing ? TensionRiseRate : TensionDecayRate;

        _smoothedTension  = Mathf.Lerp(_smoothedTension, target, rate);
        NormalizedTension = _smoothedTension;
        HighTensionAlert  = NormalizedTension > TensionThreshold;

        // Keep the 3D heatmap in sync with the gauge. SetTensionFromOCTDifference already
        // uploaded the raw per-voxel field; this rescales every voxel by the same ratio the
        // scalar just moved by and re-uploads, so the whole volume fades toward blue while
        // retracting instead of holding at whatever the raw per-frame diff happened to be.
        // Preserves the raw field's spatial pattern (which voxels are hottest relative to
        // each other) while its overall brightness tracks the directional trend.
        if (_tensionBytesR8 != null && _volW * _volH * _volD == _tensionBytesR8.Length)
        {
            float scale = raw > 1e-4f ? Mathf.Clamp01(_smoothedTension / raw) : 0f;
            for (int i = 0; i < _tensionBytesR8.Length; i++)
                _tensionBytesR8[i] = (byte)(_tensionBytesR8[i] * scale);

            UploadTensionR8(_volW, _volH, _volD);
            ConfigureMaterial();
        }
    }

    // overrides the scalar tension readout with a value from somewhere else (Python)
    public void SetExternalTension(float normalized, string warningState = null)
    {
        NormalizedTension = Mathf.Clamp01(normalized);

        if (!string.IsNullOrEmpty(warningState) && warningState != "unknown")
            HighTensionAlert = warningState == "warning" || warningState == "critical";
        else
            HighTensionAlert = NormalizedTension > TensionThreshold;
    }

    // percentile via a 256-bin histogram instead of sorting, O(n) and no allocations
    private float PercentileOf(byte[] values, float percentile)
    {
        if (values == null || values.Length == 0) return 0f;

        System.Array.Clear(_tensionHist, 0, _tensionHist.Length);
        for (int i = 0; i < values.Length; i++) _tensionHist[values[i]]++;

        int targetCount = Mathf.Max(1, Mathf.CeilToInt(values.Length * (1f - Mathf.Clamp01(percentile))));
        int cum = 0;
        for (int b = 255; b >= 0; b--)
        {
            cum += _tensionHist[b];
            if (cum >= targetCount) return b / 255f;
        }
        return 0f;
    }

    // only recreates the texture if resolution/format actually changed
    private void UploadTensionR8(int w, int h, int d)
    {
        if (_tensionTex == null || _volW != w || _volH != h || _volD != d ||
            _tensionTex.format != TextureFormat.R8)
        {
            if (_tensionTex != null) Destroy(_tensionTex);
            _tensionTex = new Texture3D(w, h, d, TextureFormat.R8, false);
            _tensionTex.wrapMode   = TextureWrapMode.Clamp;
            _tensionTex.filterMode = FilterMode.Bilinear;
            _volW = w; _volH = h; _volD = d;
        }

        _tensionTex.SetPixelData(_tensionBytesR8, 0);
        _tensionTex.Apply();
    }

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
        // heatmap covers the whole [0,1] range
        volumeMaterial.SetFloat("FeatureBandLow",  0f);
        volumeMaterial.SetFloat("FeatureBandHigh", 1f);
        // color LUT: blue -> cyan -> green -> yellow -> red (matches the course spec)
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

    // moving Gaussian blob, just for testing visuals before real data arrives
    private void UploadSyntheticTension(float t)
    {
        float cycle    = Mathf.Repeat(t * 0.3f, 1f);
        float blobX    = 0.2f + cycle * 0.6f;
        float blobY    = 0.5f;
        float blobZ    = 0.5f;
        float sigma    = 0.12f;
        float peakMag  = 0.85f + Mathf.Sin(t * 1.2f) * 0.15f; // pulse between 0.7 and 1.0

        // second weaker blob so it doesn't look so uniform
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

    // keeps a Texture2D copy of the XY plane (depth=0 slice) for the 2D overlay shader
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

        // for depth=1 (pig eye) colors already IS the 2D slice, otherwise take z=0
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
