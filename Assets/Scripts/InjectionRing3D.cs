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
    [Range(0.2f, 2.0f)] public float NeedleLength = 0.35f;
    [Range(0.002f, 0.05f)] public float NeedleThickness = 0.015f;
    [Range(0.1f, 1.0f)] public float WedgeRadius = 0.3f;

    [Header("Outer Compass Ring Options")]
    [Tooltip("Radius of the compass ring centered around the 3D volume cube.")]
    public float OuterRingRadius = 0.8f;
    [Tooltip("Target approach yaw angle in degrees (e.g. -45 is top-left).")]
    public float TargetYawAngle = -45f;
    [Tooltip("Radius/size of the direction marker spheres.")]
    public float MarkerSize = 0.05f;

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

    // Outer compass components
    private GameObject   _compassRingParent;
    private LineRenderer _outerRingLine;
    private LineRenderer _directionLine;
    private GameObject   _currentMarker;
    private GameObject   _targetMarker;
    private Material     _outerRingMat;
    private Material     _currentMarkerMat;
    private Material     _targetMarkerMat;

    // Sonar & Tether visualizers
    private Transform    _sonarVisual;
    private Material     _sonarMat;
    
    private Transform    _tetherVisual;
    private Material     _tetherMat;

    // Angle smoothing for noise reduction
    private float        _smoothedAngle = -1f;
    private float        _smoothedYaw = 0f;

    void Start()
    {
        if (RingRoot == null)
        {
            Debug.LogError("[InjectionRing3D] RingRoot is not assigned!");
            return;
        }

        if (Player == null) Player = FindObjectOfType<SubretinalSequencePlayer>();

        // 1. The Safety Target Ring (at needle tip - we will disable it in Update)
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

        // 3. The Holographic Wedge (at needle tip)
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

        // 5. Depth Tether Landing Guide
        GameObject tetherGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tetherGO.name = "DepthTetherVisual";
        tetherGO.transform.SetParent(RingRoot, false);
        Destroy(tetherGO.GetComponent<Collider>());
        _tetherVisual = tetherGO.transform;
        MeshRenderer tetherRenderer = tetherGO.GetComponent<MeshRenderer>();

        // 6. Outer Compass Ring & Target Guides (around the 3D volume cube)
        _compassRingParent = new GameObject("OuterCompassRingParent");
        
        GameObject lineGO = new GameObject("OuterRingLine");
        lineGO.transform.SetParent(_compassRingParent.transform, false);
        _outerRingLine = lineGO.AddComponent<LineRenderer>();
        _outerRingLine.useWorldSpace = false;
        _outerRingLine.startWidth = 0.015f;
        _outerRingLine.endWidth = 0.015f;
        _outerRingLine.positionCount = 37;
        
        Vector3[] circlePoints = new Vector3[37];
        for (int i = 0; i <= 36; i++)
        {
            float rad = i * 10f * Mathf.Deg2Rad;
            circlePoints[i] = new Vector3(Mathf.Sin(rad) * OuterRingRadius, 0f, Mathf.Cos(rad) * OuterRingRadius);
        }
        _outerRingLine.SetPositions(circlePoints);

        // Direction Pointer Line (Clock Hand connecting center to the current direction dot)
        GameObject dirLineGO = new GameObject("DirectionPointerLine");
        dirLineGO.transform.SetParent(_compassRingParent.transform, false);
        _directionLine = dirLineGO.AddComponent<LineRenderer>();
        _directionLine.useWorldSpace = false;
        _directionLine.startWidth = RingThickness * 0.5f;
        _directionLine.endWidth = RingThickness * 0.5f;
        _directionLine.positionCount = 2;

        // Target Direction Guide (Green Sphere - represents optimal alignment angle)
        _targetMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _targetMarker.name = "TargetDirectionMarker";
        _targetMarker.transform.SetParent(_compassRingParent.transform, false);
        Destroy(_targetMarker.GetComponent<Collider>());
        _targetMarker.transform.localScale = Vector3.one * MarkerSize * 1.5f;

        // Current Direction Guide (White Sphere - represents actual needle angle)
        _currentMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _currentMarker.name = "CurrentDirectionMarker";
        _currentMarker.transform.SetParent(_compassRingParent.transform, false);
        Destroy(_currentMarker.GetComponent<Collider>());
        _currentMarker.transform.localScale = Vector3.one * MarkerSize;

        // Setup materials (unlit depth-ignoring overlay shaders)
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

        _outerRingMat = new Material(Shader.Find("GUI/Text Shader"));
        _outerRingMat.renderQueue = 4000;

        _targetMarkerMat = new Material(Shader.Find("GUI/Text Shader"));
        _targetMarkerMat.color = SafeColor;
        _targetMarkerMat.renderQueue = 4000;

        _currentMarkerMat = new Material(Shader.Find("GUI/Text Shader"));
        _currentMarkerMat.color = Color.white;
        _currentMarkerMat.renderQueue = 4000;

        _ringRenderer.material = _ringMat;
        needleRenderer.material = _needleMat;
        wedgeRenderer.material = _wedgeMat;
        sonarRenderer.material = _sonarMat;
        tetherRenderer.material = _tetherMat;
        
        _outerRingLine.material = _outerRingMat;
        _directionLine.material = _outerRingMat;
        _targetMarker.GetComponent<MeshRenderer>().material = _targetMarkerMat;
        _currentMarker.GetComponent<MeshRenderer>().material = _currentMarkerMat;

        // Position the target marker at the target yaw angle (e.g. -45 degrees)
        float targetRad = TargetYawAngle * Mathf.Deg2Rad;
        _targetMarker.transform.localPosition = new Vector3(Mathf.Sin(targetRad) * OuterRingRadius, 0f, Mathf.Cos(targetRad) * OuterRingRadius);

        RingRoot.gameObject.SetActive(false);
    }

    void Update()
    {
        if (RingRoot == null || Player == null || _ringRenderer == null) return;

        // Hide until first frame loads
        if (Player.CannulaTipWorld == Vector3.zero)
        {
            RingRoot.gameObject.SetActive(false);
            if (_compassRingParent != null) _compassRingParent.SetActive(false);
            return;
        }
        
        RingRoot.gameObject.SetActive(true);

        // Move to cannula tip position. We calculate the world position using Player.VolumeTransform.TransformPoint
        // but keep RingRoot parented to null so it does not inherit the parent's non-uniform scaling (shearing).
        if (RingRoot.parent != null)
        {
            RingRoot.SetParent(null, false);
        }
        if (Player.VolumeTransform != null)
        {
            RingRoot.position = Player.VolumeTransform.TransformPoint(Player.CannulaTipLocal);
        }
        else
        {
            RingRoot.position = Player.CannulaTipWorld;
        }
        RingRoot.rotation = Quaternion.identity;
        RingRoot.localScale = Vector3.one;

        float angle = Player.InjectionAngleDeg;
        
        // Calculate needle's horizontal entry direction (yaw) from 2D keypoints
        float dx = Player.CannulaTip2D.x - Player.CannulaStart2D.x;
        float dy = -(Player.CannulaTip2D.y - Player.CannulaStart2D.y); // invert Y for Unity space
        float yawAngle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;

        // Safeguard against NaN/Infinity values to prevent breaking Unity's renderer bounds (worldAABB)
        if (float.IsNaN(yawAngle) || float.IsInfinity(yawAngle)) yawAngle = 0f;
        if (float.IsNaN(angle) || float.IsInfinity(angle)) angle = 0f;

        // Smooth angle and yaw to eliminate high-frequency tracker jitter/wobble
        if (angle >= 0f)
        {
            if (_smoothedAngle < 0f) // initialize first frame
            {
                _smoothedAngle = angle;
                _smoothedYaw = yawAngle;
            }
            else
            {
                // Smooth by Lerping (EMA low-pass filter)
                _smoothedAngle = Mathf.LerpAngle(_smoothedAngle, angle, Time.deltaTime * 5f);
                _smoothedYaw = Mathf.LerpAngle(_smoothedYaw, yawAngle, Time.deltaTime * 5f);
            }
        }
        else
        {
            _smoothedAngle = -1f;
        }

        // Update Needle Shaft Rotation (Yaw and Pitch/Tilt) so the silver needle matches actual tracking.
        // We rotate relative to the EnfacePlane's rotation so that the 3D needle orientation matches 
        // the 2D microscope camera view coordinates perfectly.
        if (_needlePointer != null)
        {
            if (_smoothedAngle >= 0f)
            {
                _needlePointer.rotation = Player.EnfacePlane.rotation * Quaternion.Euler(0f, _smoothedYaw + 180f, 0f) * Quaternion.Euler(_smoothedAngle, 0f, 0f);
            }
            else
            {
                _needlePointer.rotation = Quaternion.identity;
            }
        }

        // Calculate safety color coding (Angle vs Depth) using smoothed values
        Color angleColor = AngleToColor(_smoothedAngle); 
        Color depthColor = DepthToColor(Player.CannulaTipDepthMM, Player.ILMDistanceMM, angleColor); 

        // ── Hide Needle-Tip Overlays to prevent blocking surgeon's view ────────
        if (_ringRenderer != null) _ringRenderer.gameObject.SetActive(false);
        if (_wedgeGO != null) _wedgeGO.SetActive(false);
        if (_sonarVisual != null) _sonarVisual.gameObject.SetActive(false);
        if (_tetherVisual != null) _tetherVisual.gameObject.SetActive(false);

        // ── Render Compass HUD (centered at the needle tip) ────────────────────
        if (_compassRingParent != null)
        {
            if (_compassRingParent.transform.parent != RingRoot)
            {
                _compassRingParent.transform.SetParent(RingRoot, false);
            }
            _compassRingParent.transform.localPosition = Vector3.zero;
            
            // Lock rotation to match the EnfacePlane so it stays aligned with the microscope camera coordinate axes
            if (Player.EnfacePlane != null)
            {
                _compassRingParent.transform.rotation = Player.EnfacePlane.rotation;
            }
            else
            {
                _compassRingParent.transform.rotation = Quaternion.identity;
            }
            
            _compassRingParent.transform.localScale = Vector3.one;
            _compassRingParent.SetActive(true);
        }

        // Redraw/update outer circle points in case radius is adjusted at runtime
        if (_outerRingLine != null)
        {
            Vector3[] circlePoints = new Vector3[37];
            for (int i = 0; i <= 36; i++)
            {
                float rad = i * 10f * Mathf.Deg2Rad;
                circlePoints[i] = new Vector3(Mathf.Sin(rad) * OuterRingRadius, 0f, Mathf.Cos(rad) * OuterRingRadius);
            }
            _outerRingLine.SetPositions(circlePoints);
            if (_outerRingMat != null) _outerRingMat.color = depthColor;
        }

        // Update target marker position (optimal yaw direction)
        if (_targetMarker != null)
        {
            float targetRad = TargetYawAngle * Mathf.Deg2Rad;
            _targetMarker.transform.localPosition = new Vector3(Mathf.Sin(targetRad) * OuterRingRadius, 0f, Mathf.Cos(targetRad) * OuterRingRadius);
            _targetMarker.transform.localScale = Vector3.one * MarkerSize * 1.5f;
            if (_targetMarkerMat != null) _targetMarkerMat.color = SafeColor;
        }

        // Update current marker position (actual yaw direction pointer) using smoothed yaw
        if (_currentMarker != null)
        {
            if (_smoothedAngle >= 0f)
            {
                float currentRad = (_smoothedYaw + 180f) * Mathf.Deg2Rad;
                _currentMarker.transform.localPosition = new Vector3(Mathf.Sin(currentRad) * OuterRingRadius, 0f, Mathf.Cos(currentRad) * OuterRingRadius);
                _currentMarker.transform.localScale = Vector3.one * MarkerSize;
                _currentMarker.SetActive(true);
            }
            else
            {
                _currentMarker.SetActive(false);
            }
        }

        // Draw flat direction pointer line from center to current marker to solve perspective illusion
        if (_directionLine != null)
        {
            if (_smoothedAngle >= 0f && _currentMarker != null)
            {
                _directionLine.SetPosition(0, Vector3.zero);
                _directionLine.SetPosition(1, _currentMarker.transform.localPosition);
                _directionLine.gameObject.SetActive(true);
            }
            else
            {
                _directionLine.gameObject.SetActive(false);
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
