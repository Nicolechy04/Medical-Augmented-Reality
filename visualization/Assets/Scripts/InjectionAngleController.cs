using UnityEngine;

/// <summary>
/// Gradient-based Normal Estimation + NPR Color Overlay
///
/// Per frame:
///   1. Segmentation texture → scan top boundary per column → local tangent → surface normal
///   2. Consecutive needle_tip positions → smoothed needle direction vector
///   3. Angle between needle direction and surface normal → deviation from ideal (90°)
///   4. Push results to InjectionAngleOverlay shader
///
/// All computation is 2D in B-scan plane (3D requires full OCT volume).
/// </summary>
public class InjectionAngleController : MonoBehaviour
{
    [Header("Material")]
    public Material OverlayMaterial;

    [Header("Angle Safety Zones (deviation from 90°)")]
    [Range(0f, 45f)] public float SafeMaxDeg    = 20f;  // green
    [Range(0f, 45f)] public float WarnMaxDeg    = 40f;  // yellow → red beyond

    [Header("Normal Estimation")]
    [Tooltip("Half-window (columns) used for finite-difference tangent")]
    [Range(1, 30)] public int TangentWindowPx = 8;

    [Header("Needle Direction Smoothing")]
    [Range(0.01f, 1f)] public float DirectionSmoothing = 0.2f;

    // ── Public outputs ────────────────────────────────────────────────────
    /// <summary>Deviation of needle angle from ideal perpendicular, degrees [0, 90].</summary>
    public float AngleDeviationDeg { get; private set; }
    /// <summary>True when deviation exceeds WarnMaxDeg.</summary>
    public bool  AngleWarning      { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────
    private Vector2 _prevNeedleTipUV = new Vector2(0.5f, 0.5f);
    private Vector2 _smoothNeedleDir = Vector2.down;
    private bool    _hasPrev;

    private int[] _boundaryY;   // topmost non-zero pixel per column (reused buffer)
    private Color32[] _segPixels;

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Call every frame from PigEyeSequencePlayer (after loading segmentation).
    /// needleTipUV: (u, v) in flipped UV space (same as TensionOverlay2D convention).
    /// </summary>
    public void UpdateAngle(Texture2D segTex,
                            Vector2   needleTipUV,
                            int       bscanW,
                            int       bscanH)
    {
        if (OverlayMaterial == null) return;

        int w = segTex != null ? segTex.width  : bscanW;
        int h = segTex != null ? segTex.height : bscanH;

        // ── 1. Surface normal from segmentation ───────────────────────────
        Vector2 surfaceNormal = Vector2.up; // fallback: assume flat horizontal retina
        if (segTex != null)
            surfaceNormal = EstimateSurfaceNormal(segTex, needleTipUV, w, h);

        // ── 2. Needle direction from trajectory ───────────────────────────
        if (_hasPrev)
        {
            Vector2 rawDir = needleTipUV - _prevNeedleTipUV;
            if (rawDir.sqrMagnitude > 1e-8f)
            {
                rawDir.Normalize();
                _smoothNeedleDir = Vector2.Lerp(_smoothNeedleDir, rawDir,
                                                DirectionSmoothing).normalized;
            }
        }
        _prevNeedleTipUV = needleTipUV;
        _hasPrev = true;

        // ── 3. Angle deviation ────────────────────────────────────────────
        float cosAngle = Mathf.Abs(Vector2.Dot(_smoothNeedleDir, surfaceNormal));
        float angleDeg = Mathf.Acos(Mathf.Clamp01(cosAngle)) * Mathf.Rad2Deg;
        // deviation from ideal 90° (perpendicular insertion)
        AngleDeviationDeg = Mathf.Abs(90f - angleDeg);
        AngleWarning      = AngleDeviationDeg > WarnMaxDeg;

        // ── 4. Push to shader ─────────────────────────────────────────────
        if (OverlayMaterial != null)
        {
            OverlayMaterial.SetVector("_NeedleTipUV",    new Vector4(needleTipUV.x, needleTipUV.y, 0, 0));
            OverlayMaterial.SetVector("_SurfaceNormal",  new Vector4(surfaceNormal.x, surfaceNormal.y, 0, 0));
            OverlayMaterial.SetVector("_NeedleDir",      new Vector4(_smoothNeedleDir.x, _smoothNeedleDir.y, 0, 0));
            OverlayMaterial.SetFloat ("_AngleDeviation", AngleDeviationDeg);
            OverlayMaterial.SetFloat ("_SafeMaxDeg",     SafeMaxDeg);
            OverlayMaterial.SetFloat ("_WarnMaxDeg",     WarnMaxDeg);
        }
    }

    // ── Normal estimation ─────────────────────────────────────────────────

    private Vector2 EstimateSurfaceNormal(Texture2D seg, Vector2 tipUV, int w, int h)
    {
        // Ensure boundary buffer
        if (_boundaryY == null || _boundaryY.Length != w)
            _boundaryY = new int[w];

        // Read segmentation pixels every frame (seg texture content changes each frame)
        _segPixels = seg.GetPixels32();

        // For each column: find topmost non-zero pixel (= ILM boundary)
        // seg is loaded with Unity's y-flip: pixel (x,y) in array = (x, h-1-y) in image
        for (int x = 0; x < w; x++)
        {
            _boundaryY[x] = h; // sentinel: no boundary found
            // scan from top of image (y = h-1 in Unity texture coords)
            for (int y = h - 1; y >= 0; y--)
            {
                byte val = _segPixels[y * w + x].r;
                if (val > 0 && val < 250) // layers 1-3; ignore 253-255 (outer/noise)
                {
                    _boundaryY[x] = y;
                    break;
                }
            }
        }

        // At needle tip x, compute local tangent via finite difference
        int tipX = Mathf.RoundToInt(tipUV.x * (w - 1));
        int dx   = TangentWindowPx;
        int x0   = Mathf.Clamp(tipX - dx, 0, w - 1);
        int x1   = Mathf.Clamp(tipX + dx, 0, w - 1);

        if (_boundaryY[x0] == h || _boundaryY[x1] == h)
            return Vector2.up; // boundary not found → fallback

        // Tangent in UV space
        float duv_x  = (float)(x1 - x0) / w;
        float duv_y  = (float)(_boundaryY[x1] - _boundaryY[x0]) / h; // already in Unity y-flip space

        Vector2 tangent = new Vector2(duv_x, duv_y).normalized;
        // Normal = 90° CCW from tangent
        Vector2 normal = new Vector2(-tangent.y, tangent.x);

        // Ensure normal points "upward" in UV space (away from tissue)
        if (normal.y < 0f) normal = -normal;
        return normal.normalized;
    }

    // ── Reset on new sequence ─────────────────────────────────────────────
    public void ResetTracking()
    {
        _hasPrev     = false;
        _smoothNeedleDir = Vector2.down;
        _segPixels   = null;
        _boundaryY   = null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(InjectionAngleController))]
public class InjectionAngleControllerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var ctrl = (InjectionAngleController)target;
        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField("Runtime State", UnityEditor.EditorStyles.boldLabel);

        var orig = UnityEditor.EditorStyles.label.normal.textColor;
        UnityEditor.EditorStyles.label.normal.textColor =
            ctrl.AngleWarning ? Color.red : Color.green;
        UnityEditor.EditorGUILayout.LabelField("Angle Deviation",
            $"{ctrl.AngleDeviationDeg:F1}°  {(ctrl.AngleWarning ? "⚠ WARNING" : "OK")}");
        UnityEditor.EditorStyles.label.normal.textColor = orig;
    }
}
#endif
