using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Injection angle from real data.
///
/// Pipeline:
///   1. Map the cannula tip (world space) into the loaded OCT volume's local
///      frame via the iOCT Microscope's own transform (fundus-to-OCT
///      registration for this dataset is known ground truth).
///   2. Convert that local position to a voxel index using the volume's known
///      physical size, and estimate the local surface normal there via a
///      central-difference gradient on the volume intensity data.
///   3. Estimate needle direction from a smoothed tip trajectory — a single
///      frame-to-frame delta is too noisy (confirmed empirically: adjacent
///      frames during steady insertion swing wildly), so this uses a rolling
///      window instead.
///   4. angle-from-tangent = 90 - arccos(|normal . direction|).
///
/// Two things here are provisional and exposed in the inspector on purpose:
///   - The local-axis -> voxel-axis mapping (needs a one-time calibration:
///     drop DebugMarker inside the volume and check it lines up with a known
///     needle shadow, e.g. frame 00020 / B-scan slice ~295).
///   - The safe-range thresholds (25-45 deg is a placeholder pending
///     confirmation of the convention actually used in the course).
/// </summary>
public class RealInjectionAngleController : MonoBehaviour
{
    [Header("Source")]
    public SubretinalSequencePlayer Player;

    [Header("Volume Physical Size (mm)")]
    public float LateralExtentMM = 30f; // WidthAndHeightInMM
    public float DepthExtentMM   = 40f; // DepthInMM

    public enum LocalAxis { X, Y, Z }

    [Header("Axis Mapping (local -> volume) — calibrate against a known frame")]
    [Tooltip("Which local-space axis maps to depth (B-scan row / A-scan direction)")]
    public LocalAxis DepthAxis = LocalAxis.Y;
    [Tooltip("Which local-space axis maps to the in-slice column (B-scan width)")]
    public LocalAxis ColumnAxis = LocalAxis.X;
    [Tooltip("Which local-space axis maps to the slice index (stack direction)")]
    public LocalAxis SliceAxis = LocalAxis.Z;

    [Header("Needle Direction Smoothing")]
    [Tooltip("How many frames back to look for a stable trajectory baseline")]
    [Range(2, 20)] public int TrajectoryWindowFrames = 8;

    [Header("Safe Range (degrees from tangent plane) — PROVISIONAL, confirm with TA")]
    public float SafeMinDeg    = 25f;
    public float SafeMaxDeg    = 45f;
    public float WarnMarginDeg = 10f;

    [Header("Manual Override (demo)")]
    [Tooltip("Real recorded data barely varies in angle — use this to demonstrate the Caution/Danger states on demand.")]
    public bool  ManualOverride = false;
    [Range(0f, 90f)] public float ManualAngleDeg = 30f;

    [Header("Debug")]
    [Tooltip("Optional marker, parented under the volume cube, dropped at the computed voxel position for calibration.")]
    public Transform DebugMarker;

    public enum SafetyState { Safe, Caution, Danger }

    public float       AngleFromTangentDeg { get; private set; }
    public SafetyState State               { get; private set; }
    public bool         HasValidReading    { get; private set; }

    readonly Queue<Vector3> _tipHistory = new Queue<Vector3>();

    void Update()
    {
        if (ManualOverride)
        {
            AngleFromTangentDeg = ManualAngleDeg;
            State            = Classify(ManualAngleDeg);
            HasValidReading  = true;
            return;
        }

        if (Player == null || Player.VolumeBytes == null)
        {
            HasValidReading = false;
            return;
        }

        Vector3 tip = Player.CannulaTipWorld;
        _tipHistory.Enqueue(tip);
        while (_tipHistory.Count > TrajectoryWindowFrames) _tipHistory.Dequeue();
        if (_tipHistory.Count < 2)
        {
            HasValidReading = false;
            return;
        }

        Vector3 delta = tip - _tipHistory.Peek();
        if (delta.magnitude < 1e-4f)
        {
            HasValidReading = false;
            return;
        }
        Vector3 needleDir = delta.normalized;

        Vector3 local = Quaternion.Inverse(Player.IOCTRotation) * (tip - Player.IOCTOrigin);

        int vx, vy, vz;
        LocalToVoxel(local, out vx, out vy, out vz);

        if (DebugMarker != null)
        {
            DebugMarker.localPosition = new Vector3(
                (vx / (float)Player.VolumeVoxelW) - 0.5f,
                (vy / (float)Player.VolumeVoxelH) - 0.5f,
                (vz / (float)Player.VolumeVoxelD) - 0.5f);
        }

        Vector3 gradient = SampleGradient(Player.VolumeBytes, Player.VolumeVoxelW, Player.VolumeVoxelH, Player.VolumeVoxelD, vx, vy, vz);
        if (gradient.sqrMagnitude < 1e-8f)
        {
            HasValidReading = false;
            return;
        }
        Vector3 normal = gradient.normalized;

        float cosAlpha = Mathf.Abs(Vector3.Dot(normal, needleDir));
        float alpha    = Mathf.Acos(Mathf.Clamp(cosAlpha, -1f, 1f)) * Mathf.Rad2Deg;

        AngleFromTangentDeg = 90f - alpha;
        State           = Classify(AngleFromTangentDeg);
        HasValidReading = true;
    }

    SafetyState Classify(float theta)
    {
        if (theta >= SafeMinDeg && theta <= SafeMaxDeg) return SafetyState.Safe;
        if (theta >= SafeMinDeg - WarnMarginDeg && theta <= SafeMaxDeg + WarnMarginDeg) return SafetyState.Caution;
        return SafetyState.Danger;
    }

    void LocalToVoxel(Vector3 local, out int vx, out int vy, out int vz)
    {
        float depthVal = Component(local, DepthAxis);
        float colVal   = Component(local, ColumnAxis);
        float sliceVal = Component(local, SliceAxis);

        // Depth axis assumed to span [0, DepthExtentMM]; lateral axes span [-L/2, L/2].
        float u = Mathf.Clamp01((colVal   + LateralExtentMM * 0.5f) / LateralExtentMM);
        float v = Mathf.Clamp01(depthVal  / DepthExtentMM);
        float s = Mathf.Clamp01((sliceVal + LateralExtentMM * 0.5f) / LateralExtentMM);

        vx = Mathf.Clamp(Mathf.RoundToInt(u * (Player.VolumeVoxelW - 1)), 0, Player.VolumeVoxelW - 1);
        vy = Mathf.Clamp(Mathf.RoundToInt(v * (Player.VolumeVoxelH - 1)), 0, Player.VolumeVoxelH - 1);
        vz = Mathf.Clamp(Mathf.RoundToInt(s * (Player.VolumeVoxelD - 1)), 0, Player.VolumeVoxelD - 1);
    }

    static float Component(Vector3 v, LocalAxis axis)
    {
        if (axis == LocalAxis.X) return v.x;
        if (axis == LocalAxis.Y) return v.y;
        return v.z;
    }

    static Vector3 SampleGradient(byte[] vol, int w, int h, int d, int x, int y, int z)
    {
        x = Mathf.Clamp(x, 1, w - 2);
        y = Mathf.Clamp(y, 1, h - 2);
        z = Mathf.Clamp(z, 1, d - 2);
        float dx = P(vol, w, h, x + 1, y, z) - P(vol, w, h, x - 1, y, z);
        float dy = P(vol, w, h, x, y + 1, z) - P(vol, w, h, x, y - 1, z);
        float dz = P(vol, w, h, x, y, z + 1) - P(vol, w, h, x, y, z - 1);
        return new Vector3(dx, dy, dz);
    }

    static float P(byte[] vol, int w, int h, int x, int y, int z) => vol[z * w * h + y * w + x];
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(RealInjectionAngleController))]
public class RealInjectionAngleControllerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var ctrl = (RealInjectionAngleController)target;
        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField("Runtime State", UnityEditor.EditorStyles.boldLabel);

        if (!ctrl.HasValidReading)
        {
            UnityEditor.EditorGUILayout.HelpBox("No valid reading yet (need a few frames of trajectory history).", UnityEditor.MessageType.Info);
            return;
        }

        Color prev = UnityEditor.EditorStyles.label.normal.textColor;
        UnityEditor.EditorStyles.label.normal.textColor = ctrl.State switch
        {
            RealInjectionAngleController.SafetyState.Safe    => Color.green,
            RealInjectionAngleController.SafetyState.Caution => new Color(1f, 0.6f, 0f),
            _                                                 => Color.red,
        };
        UnityEditor.EditorGUILayout.LabelField("Angle (from tangent)", $"{ctrl.AngleFromTangentDeg:F1} deg — {ctrl.State}");
        UnityEditor.EditorStyles.label.normal.textColor = prev;
    }
}
#endif
