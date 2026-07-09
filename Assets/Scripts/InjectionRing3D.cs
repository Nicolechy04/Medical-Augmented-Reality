using UnityEngine;

/// <summary>
/// Holographic Angle Guidance System for Lecture Presentation.
/// Features:
///   1. Silver Needle Shaft: Metallic cannula passing through the volume.
///   2. Dynamic Target Ring: Color-coded circle around the needle tip.
///   3. Holographic Angle Wedge: Dynamic glowing wedge showing insertion angle.
///   4. Interactive Sonar Pulse: Concentric wave that pulses faster as the needle
///      gets closer to the retina (visual Geiger-counter concept).
///   5. Depth Tether: Glowing landing guide line showing distance remaining.
/// </summary>
public class InjectionRing3D : MonoBehaviour
{
    [Header("Scene Reference")]
    [Tooltip("Empty GO moved to cannula tip each frame. Attach nothing else to it.")]
    public Transform RingRoot;

    [Tooltip("SubretinalSequencePlayer — provides CannulaTipWorld, InjectionAngleDeg, and ILMDistanceMM.")]
    public SubretinalSequencePlayer Player;

    [Header("HUD Dimensions")]
    [Range(0.02f, 0.5f)] public float RingRadius = 0.12f;
    [Range(0.001f, 0.1f)] public float RingThickness = 0.015f;
    [Range(0.2f, 2.0f)] public float NeedleLength = 1.0f;
    [Range(0.002f, 0.05f)] public float NeedleThickness = 0.015f;
    [Range(0.1f, 1.0f)] public float WedgeRadius = 0.3f;

    [Header("Colors")]
    public Color SafeColor    = new Color(0.10f, 0.90f, 0.20f, 0.6f);   // glowing green
    public Color CautionColor = new Color(1.00f, 0.78f, 0.00f, 0.6f);   // glowing amber
    public Color DangerColor  = new Color(0.95f, 0.15f, 0.15f, 0.6f);   // glowing red
    public Color NoDataColor  = new Color(0.55f, 0.55f, 0.55f, 0.4f);   // glowing grey
    public Color NeedleColor  = new Color(0.85f, 0.85f, 0.90f, 0.9f);   // silver needle
    public Color TetherColor  = new Color(0.20f, 0.60f, 1.00f, 0.5f);   // glowing blue depth tethers

    [Header("Angle Safety Zones (degrees from vertical)")]
    public float GreenMin  = 43f;   // Narrowed safety tolerance so that the real
    public float GreenMax  = 47f;   // needle angle (45-47) naturally changes states
    public float YellowBand = 5f;    // during play, creating a visible demo.

    // ── private ───────────────────────────────────────────────────────────────
    private MeshRenderer _ringRenderer;
    private Transform    _needlePointer;
    private Material     _ringMat;
    private Material     _needleMat;
    private Material     _wedgeMat;
    
    // Holographic wedge mesh generation
    private GameObject   _wedgeGO;
    private MeshFilter   _wedgeMeshFilter;
    private Mesh         _wedgeMesh;

    // Sonar & Tether visualizers
    private Transform    _sonarVisual;
    private Material     _sonarMat;
    
    private Transform    _tetherVisual;
    private Material     _tetherMat;

    void Start()
    {
        if (RingRoot == null)
        {
            Debug.LogError("[InjectionRing3D] RingRoot is not assigned!");
            return;
        }

        if (Player == null) Player = FindObjectOfType<SubretinalSequencePlayer>();

        // 1. The Safety Target Ring
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "SafetyRingVisual";
        cylinder.transform.SetParent(RingRoot, false);
        Destroy(cylinder.GetComponent<Collider>());
        cylinder.transform.localScale = new Vector3(RingRadius * 2f, RingThickness, RingRadius * 2f);
        _ringRenderer = cylinder.GetComponent<MeshRenderer>();

        // 2. The Tilted Needle Pivot & Shaft
        GameObject pointerPivot = new GameObject("NeedlePointerPivot");
        pointerPivot.transform.SetParent(RingRoot, false);
        _needlePointer = pointerPivot.transform;

        GameObject pointerCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pointerCyl.name = "NeedleShaftVisual";
        pointerCyl.transform.SetParent(_needlePointer, false);
        Destroy(pointerCyl.GetComponent<Collider>());
        
        pointerCyl.transform.localPosition = new Vector3(0f, NeedleLength * 0.5f, 0f);
        pointerCyl.transform.localScale = new Vector3(NeedleThickness * 2f, NeedleLength * 0.5f, NeedleThickness * 2f);
        MeshRenderer needleRenderer = pointerCyl.GetComponent<MeshRenderer>();

        // 3. The Holographic Wedge
        _wedgeGO = new GameObject("HolographicAngleWedge");
        _wedgeGO.transform.SetParent(RingRoot, false);
        _wedgeMeshFilter = _wedgeGO.AddComponent<MeshFilter>();
        MeshRenderer wedgeRenderer = _wedgeGO.AddComponent<MeshRenderer>();
        _wedgeMesh = new Mesh();
        _wedgeMeshFilter.mesh = _wedgeMesh;

        // 4. Sonar Radar Pulse Ring (flat, thin cylinder that expands)
        GameObject sonarGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        sonarGO.name = "SonarPulseVisual";
        sonarGO.transform.SetParent(RingRoot, false);
        Destroy(sonarGO.GetComponent<Collider>());
        _sonarVisual = sonarGO.transform;
        MeshRenderer sonarRenderer = sonarGO.GetComponent<MeshRenderer>();

        // 5. Depth Tether Landing Guide (thin vertical column stretching down to retina)
        GameObject tetherGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tetherGO.name = "DepthTetherVisual";
        tetherGO.transform.SetParent(RingRoot, false);
        Destroy(tetherGO.GetComponent<Collider>());
        _tetherVisual = tetherGO.transform;
        MeshRenderer tetherRenderer = tetherGO.GetComponent<MeshRenderer>();

        // Setup transparent overlay materials using GUI/Text Shader (ignores depth testing, renders on top)
        _ringMat = new Material(Shader.Find("GUI/Text Shader"));
        _ringMat.renderQueue = 4000;

        _needleMat = new Material(Shader.Find("GUI/Text Shader"));
        _needleMat.color = NeedleColor;
        _needleMat.renderQueue = 4000;

        _wedgeMat = new Material(Shader.Find("GUI/Text Shader"));
        _wedgeMat.renderQueue = 4000;

        _sonarMat = new Material(Shader.Find("GUI/Text Shader"));
        _sonarMat.renderQueue = 4000;

        _tetherMat = new Material(Shader.Find("GUI/Text Shader"));
        _tetherMat.color = TetherColor;
        _tetherMat.renderQueue = 4000;

        _ringRenderer.material = _ringMat;
        needleRenderer.material = _needleMat;
        wedgeRenderer.material = _wedgeMat;
        sonarRenderer.material = _sonarMat;
        tetherRenderer.material = _tetherMat;

        RingRoot.gameObject.SetActive(false);
    }

    void Update()
    {
        if (RingRoot == null || Player == null || _ringRenderer == null) return;

        // Hide until first frame loads
        if (Player.CannulaTipWorld == Vector3.zero)
        {
            RingRoot.gameObject.SetActive(false);
            return;
        }
        RingRoot.gameObject.SetActive(true);

        // Parent to volume for coordinate space synchronization
        if (Player.VolumeTransform != null && RingRoot.parent != Player.VolumeTransform)
        {
            RingRoot.SetParent(Player.VolumeTransform, false);
            RingRoot.localScale = Vector3.one;
        }

        // Move to cannula tip local position
        RingRoot.localPosition = Player.CannulaTipLocal;
        RingRoot.localRotation = Quaternion.identity;

        float angle = Player.InjectionAngleDeg;
        
        // Calculate needle's horizontal entry direction (yaw) from 2D keypoints
        float dx = Player.CannulaTip2D.x - Player.CannulaStart2D.x;
        float dy = -(Player.CannulaTip2D.y - Player.CannulaStart2D.y); // invert Y for Unity space
        float yawAngle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;

        // Safeguard against NaN/Infinity values to prevent breaking Unity's renderer bounds (worldAABB)
        if (float.IsNaN(yawAngle) || float.IsInfinity(yawAngle)) yawAngle = 0f;
        if (float.IsNaN(angle) || float.IsInfinity(angle)) angle = 0f;

        // Update Needle Shaft Rotation (Yaw and Pitch/Tilt)
        if (_needlePointer != null)
        {
            if (angle >= 0f)
            {
                // Rotate by yawAngle + 180 degrees so the virtual shaft extends backwards/outwards from the eye
                _needlePointer.localRotation = Quaternion.Euler(0f, yawAngle + 180f, 0f) * Quaternion.Euler(angle, 0f, 0f);
            }
            else
            {
                _needlePointer.localRotation = Quaternion.identity;
            }
        }

        // Apply separated safety color coding (Angle vs Depth)
        Color angleColor = AngleToColor(angle); // Always Green/Yellow/Red based on tilt angle
        Color depthColor = DepthToColor(Player.CannulaTipDepthMM, Player.ILMDistanceMM, angleColor); // Cyan/Red based on target depth

        if (_ringMat != null) _ringMat.color = depthColor; // Target Ring represents Depth safety
        if (_wedgeMat != null) _wedgeMat.color = angleColor; // Holographic Wedge represents Angle safety

        // ── Holographic wedge ─────────────────────────────────────────────────
        // Only generate wedge when angle > 0.5f degrees. If angle is 0, mesh vertices collapse
        // to a single line, generating NaN normals in RecalculateNormals() that break the renderer.
        if (angle > 0.5f && _wedgeMesh != null)
        {
            _wedgeGO.SetActive(true);
            GenerateWedgeMesh(angle, yawAngle);
        }
        else if (_wedgeGO != null)
        {
            _wedgeGO.SetActive(false);
        }

        // ── Sonar Radar Pulse ─────────────────────────────────────────────────
        if (_sonarVisual != null && _sonarMat != null)
        {
            float dist = Player.ILMDistanceMM;
            if (float.IsNaN(dist) || float.IsInfinity(dist) || dist < 0f) dist = 20f; // safety fallback

            // Pulse frequency increases as distance decreases (from 1Hz when far to 8Hz when touching)
            float pulseSpeed = Mathf.Lerp(8f, 1f, Mathf.Clamp01(dist / 20f));
            float pulseTime = (Time.time * pulseSpeed) % 1.0f;

            // Sonar ring expands outwards and fades out
            float scale = RingRadius * (1.0f + pulseTime * 1.5f);
            _sonarVisual.localScale = new Vector3(scale * 2f, RingThickness * 0.4f, scale * 2f);
            
            Color sonarColor = depthColor;
            sonarColor.a = (1.0f - pulseTime) * 0.4f; // fade out transparency
            _sonarMat.color = sonarColor;
        }

        // ── Depth Tether Landing Guide ────────────────────────────────────────
        if (_tetherVisual != null)
        {
            float dist = Player.ILMDistanceMM;
            if (float.IsNaN(dist) || float.IsInfinity(dist)) dist = -1f; // safety fallback
            
            // Render tether only when above the retina (positive distance)
            if (dist > 0.05f)
            {
                _tetherVisual.gameObject.SetActive(true);

                // Prevent division by zero if DepthExtentMM is not initialized
                float depthExtent = Player.DepthExtentMM > 0.01f ? Player.DepthExtentMM : 40f;

                // Calculate tether bounds in local volume Y coordinates
                float tetherTopY = RingRoot.localPosition.y; // visual tip (clamped to 0.5f if high above)

                // ILM surface local Y based on physical needle depth and distance to retina
                float realNeedleDepth = Player.CannulaTipDepthMM;
                float ilmLocalY = 0.5f - (realNeedleDepth + dist) / depthExtent;

                // The vertical length of the tether in local units
                float tetherLengthLocal = Mathf.Max(0f, tetherTopY - ilmLocalY);

                // Position tether cylinder so its center is halfway between visual tip and ILM surface
                _tetherVisual.localPosition = new Vector3(0f, -tetherLengthLocal * 0.5f, 0f);
                // Make the tether thickness 0.012f (4x thicker than before) so it is clearly visible
                _tetherVisual.localScale = new Vector3(0.012f, tetherLengthLocal * 0.5f, 0.012f);
            }
            else
            {
                _tetherVisual.gameObject.SetActive(false); // hide when touched down
            }
        }
    }

    void GenerateWedgeMesh(float angle, float yawAngle)
    {
        _wedgeMesh.Clear();

        int segments = 16;
        Vector3[] vertices = new Vector3[segments + 2];
        int[] triangles = new int[segments * 6]; // double triangles for backfaces

        vertices[0] = Vector3.zero;
        Quaternion yawRot = Quaternion.Euler(0f, yawAngle, 0f);

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float currentAngle = t * angle;
            Vector3 pointDir = Quaternion.Euler(currentAngle, 0f, 0f) * Vector3.up;
            vertices[i + 1] = yawRot * (pointDir * WedgeRadius);
        }

        // Build double-sided triangles so the wedge is visible from both sides
        for (int i = 0; i < segments; i++)
        {
            int tIdx = i * 6;
            // Front side (clockwise)
            triangles[tIdx]     = 0;
            triangles[tIdx + 1] = i + 1;
            triangles[tIdx + 2] = i + 2;

            // Back side (counter-clockwise)
            triangles[tIdx + 3] = 0;
            triangles[tIdx + 4] = i + 2;
            triangles[tIdx + 5] = i + 1;
        }

        _wedgeMesh.vertices = vertices;
        _wedgeMesh.triangles = triangles;
        _wedgeMesh.RecalculateNormals();
        _wedgeMesh.RecalculateBounds();
    }

    Color AngleToColor(float deg)
    {
        // Wedge always represents the angle safety (from vertical reference)
        if (deg < 0f) return NoDataColor;
        if (deg >= GreenMin && deg <= GreenMax) return SafeColor;
        float yMin = GreenMin - YellowBand;
        float yMax = GreenMax + YellowBand;
        if (deg >= yMin && deg <= yMax) return CautionColor;
        return DangerColor;
    }

    Color DepthToColor(float depth, float ilmDist, Color fallbackAngleColor)
    {
        // Target Ring represents the depth safety:
        // Scanner top is at 0.0mm. Scanner bottom is at 40.0mm.
        // ILM surface lies at depth ~20.0mm. RPE base layer lies at depth ~22.0mm.
        if (depth > 0.01f)
        {
            // State 1: Punctured RPE (Danger! Too Deep!)
            if (depth >= 22.0f)
            {
                return DangerColor; // solid warning alarm red
            }
            // State 2: Inside Subretinal Space (Safe Injection Zone!)
            if (depth >= 20.0f || ilmDist < 0f)
            {
                return new Color(0.00f, 0.80f, 1.00f, 0.70f); // Electric Cyan
            }
        }

        // State 3: Approaching (above retina) -> ring matches angle safety color for redundancy
        return fallbackAngleColor;
    }
}
