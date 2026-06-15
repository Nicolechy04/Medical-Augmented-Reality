using UnityEngine;

/// <summary>
/// Injection Angle Visualization — 3D OCT Volume approach.
///
/// - Generates a synthetic 512x512x49 OCT volume with curved retinal layers
/// - Pushes the volume to InjectionAngle3D.shader every frame
/// - Computes surface normal via Central Difference on the CPU at the needle tip
/// - Computes angle deviation = |90 - arccos(n . d)| in 3D
/// - Real-time control: drag TiltX / TiltY sliders in Inspector
/// </summary>
public class InjectionAngle3DController : MonoBehaviour
{
    [Header("Material")]
    public Material OverlayMaterial;

    [Header("Synthetic Volume")]
    public bool GenerateOnStart = true;
    [Tooltip("Must match shader 3D texture size")]
    public int VolumeW = 512;
    public int VolumeH = 512;
    public int VolumeD = 49;

    [Header("Needle Direction — drag these sliders")]
    [Range(-45f, 45f)] public float TiltX = 0f;   // left / right tilt
    [Range(-45f, 45f)] public float TiltY = 0f;   // forward / back tilt

    [Header("Needle Tip Position")]
    [Range(0f, 1f)] public float TipU  = 0.5f;
    [Range(0f, 1f)] public float TipV  = 0.5f;
    [Range(0f, 1f)] public float SliceZ = 0.5f;

    [Header("Angle Safety Zones (degrees)")]
    public float SafeMaxDeg    = 10f;
    public float CautionMaxDeg = 20f;

    [Header("Display")]
    [Range(0.02f, 0.3f)] public float ArcRadius     = 0.12f;
    [Range(0f,    1f)]   public float ShowGradField = 0.25f;

    // ── Public outputs ──────────────────────────────────────────────────────
    public float   AngleDeviationDeg { get; private set; }
    public bool    AngleWarning      { get; private set; }
    public Vector3 SurfaceNormal3D   { get; private set; }
    public Vector3 NeedleDir3D       { get; private set; }

    // ── Private ─────────────────────────────────────────────────────────────
    private Texture3D _volume;
    private float[]   _pixels;  // CPU-side float[] for gradient sampling (RFloat = 1 float/pixel)

    // ── Lifecycle ────────────────────────────────────────────────────────────

    void Start()
    {
        if (GenerateOnStart)
            GenerateSyntheticVolume();
    }

    void Update()
    {
        if (OverlayMaterial == null || _volume == null) return;

        // Needle direction from inspector tilt angles
        float rx = TiltX * Mathf.Deg2Rad;
        float ry = TiltY * Mathf.Deg2Rad;
        NeedleDir3D = new Vector3(
            Mathf.Sin(rx),
            Mathf.Sin(ry),
            -Mathf.Cos(rx) * Mathf.Cos(ry)
        ).normalized;

        // Surface normal from CPU gradient at needle tip
        Vector3 grad = SampleGradientCPU(TipU, TipV, SliceZ);
        SurfaceNormal3D = grad.sqrMagnitude > 1e-8f ? grad.normalized : Vector3.up;

        // Angle deviation in 3D: alpha = arccos(|n . d|), deviation = |90 - alpha|
        float cosA = Mathf.Abs(Vector3.Dot(SurfaceNormal3D, NeedleDir3D));
        float alphaDeg = Mathf.Acos(Mathf.Clamp01(cosA)) * Mathf.Rad2Deg;
        AngleDeviationDeg = Mathf.Abs(90f - alphaDeg);
        AngleWarning      = AngleDeviationDeg > CautionMaxDeg;

        // Push everything to the shader
        OverlayMaterial.SetTexture("_VolumeTex",
            _volume);
        OverlayMaterial.SetFloat("_SliceZ",
            SliceZ);
        OverlayMaterial.SetFloat("_Brightness",
            3.0f);
        OverlayMaterial.SetVector("_NeedleTipUV",
            new Vector4(TipU, TipV, 0, 0));
        OverlayMaterial.SetVector("_NeedleDir",
            new Vector4(NeedleDir3D.x, NeedleDir3D.y, NeedleDir3D.z, 0));
        OverlayMaterial.SetVector("_SurfaceNormal",
            new Vector4(SurfaceNormal3D.x, SurfaceNormal3D.y, SurfaceNormal3D.z, 0));
        OverlayMaterial.SetFloat("_AngleDeviation",
            AngleDeviationDeg);
        OverlayMaterial.SetFloat("_SafeMaxDeg",    SafeMaxDeg);
        OverlayMaterial.SetFloat("_CautionMaxDeg", CautionMaxDeg);
        OverlayMaterial.SetFloat("_ArcRadius",     ArcRadius);
        OverlayMaterial.SetFloat("_ShowGradField", ShowGradField);
        OverlayMaterial.SetFloat("_AspectRatio",   1f);
    }

    // ── CPU gradient (for angle computation) ────────────────────────────────

    private Vector3 SampleGradientCPU(float u, float v, float w)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(u * (VolumeW - 1)), 1, VolumeW - 2);
        int y = Mathf.Clamp(Mathf.RoundToInt(v * (VolumeH - 1)), 1, VolumeH - 2);
        int z = Mathf.Clamp(Mathf.RoundToInt(w * (VolumeD - 1)), 1, VolumeD - 2);

        float dx = P(x+1, y, z) - P(x-1, y, z);
        float dy = P(x, y+1, z) - P(x, y-1, z);
        float dz = P(x, y, z+1) - P(x, y, z-1);
        return new Vector3(dx, dy, dz);
    }

    private float P(int x, int y, int z)
    {
        int idx = z * VolumeW * VolumeH + y * VolumeW + x;
        return (_pixels != null && (uint)idx < (uint)_pixels.Length) ? _pixels[idx] : 0f;
    }

    // ── Synthetic volume generation ──────────────────────────────────────────

    /// <summary>
    /// Creates a 512x512x49 synthetic OCT-like volume.
    /// Retinal surface: bowl shape peaking at z=0.50 in the center.
    /// Three layers: ILM (bright), inner nuclear, RPE.
    /// </summary>
    public void GenerateSyntheticVolume()
    {
        int w = VolumeW, h = VolumeH, d = VolumeD;

        // float[] uses 4x less memory than Color[] for RFloat (1 float/pixel)
        _pixels = new float[w * h * d];

        for (int z = 0; z < d; z++)
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float nx = x / (float)(w - 1);
            float ny = y / (float)(h - 1);
            float nz = z / (float)(d - 1);

            float bowl = 0.10f * Mathf.Sin(Mathf.PI * nx) * Mathf.Sin(Mathf.PI * ny);
            float s    = 0.48f + bowl;

            float I = 0f;
            I += 0.90f * Gauss(nz, s,         0.018f);  // ILM
            I += 0.45f * Gauss(nz, s + 0.12f, 0.012f);  // inner nuclear layer
            I += 0.72f * Gauss(nz, s + 0.26f, 0.016f);  // RPE

            _pixels[z * w * h + y * w + x] = Mathf.Clamp01(I);
        }

        _volume = new Texture3D(w, h, d, TextureFormat.RFloat, false);
        _volume.wrapMode   = TextureWrapMode.Clamp;
        _volume.filterMode = FilterMode.Bilinear;
        _volume.SetPixelData(_pixels, 0);
        _volume.Apply();

        Debug.Log($"[InjectionAngle3D] Synthetic volume generated: {w}x{h}x{d}");
    }

    private static float Gauss(float x, float mu, float sigma)
    {
        float d = (x - mu) / sigma;
        return Mathf.Exp(-0.5f * d * d);
    }

    void OnDestroy()
    {
        if (_volume != null) Destroy(_volume);
    }
}

// ── Custom Inspector ──────────────────────────────────────────────────────────
#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(InjectionAngle3DController))]
public class InjectionAngle3DControllerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var ctrl = (InjectionAngle3DController)target;

        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField("Runtime State", UnityEditor.EditorStyles.boldLabel);

        Color prev = UnityEditor.EditorStyles.label.normal.textColor;
        UnityEditor.EditorStyles.label.normal.textColor =
            ctrl.AngleWarning ? Color.red : Color.green;
        UnityEditor.EditorGUILayout.LabelField(
            "Angle Deviation",
            $"{ctrl.AngleDeviationDeg:F1}  deg   {(ctrl.AngleWarning ? "WARNING" : "OK")}");
        UnityEditor.EditorStyles.label.normal.textColor = prev;

        UnityEditor.EditorGUILayout.LabelField(
            "Surface Normal",
            ctrl.SurfaceNormal3D.ToString("F3"));
        UnityEditor.EditorGUILayout.LabelField(
            "Needle Dir",
            ctrl.NeedleDir3D.ToString("F3"));

        UnityEditor.EditorGUILayout.Space();
        if (GUILayout.Button("Regenerate Synthetic Volume"))
            ctrl.GenerateSyntheticVolume();
    }
}
#endif
