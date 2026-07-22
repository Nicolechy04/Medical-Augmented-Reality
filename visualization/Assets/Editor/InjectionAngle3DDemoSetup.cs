#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public static class InjectionAngle3DDemoSetup
{
    private const string MenuPath      = "TensionViz/Setup 3D Angle Demo";
    private const string MatSavePath   = "Assets/Materials/InjectionAngle3DMat.mat";
    private const string ShaderName    = "TensionViz/InjectionAngle3D";
    private const string SceneSavePath = "Assets/Scenes/InjectionAngle3DDemo.unity";

    [MenuItem(MenuPath, priority = 3)]
    public static void SetupScene()
    {
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
            UnityEditor.SceneManagement.NewSceneMode.Single);

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog("TensionViz",
                $"Shader '{ShaderName}' not found. Check Console for errors.", "OK");
            return;
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatSavePath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "InjectionAngle3DMat" };
            AssetDatabase.CreateAsset(mat, MatSavePath);
            AssetDatabase.SaveAssets();
            mat = AssetDatabase.LoadAssetAtPath<Material>(MatSavePath);
        }
        else { mat.shader = shader; }

        // Square quad — volume slice is square XY
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "VolumeSliceQuad";
        quad.transform.localScale = Vector3.one * 2f;
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Object.DestroyImmediate(quad.GetComponent<MeshCollider>());

        GameObject ctrlGO = new GameObject("AngleDemo3DController");
        InjectionAngle3DController ctrl = ctrlGO.AddComponent<InjectionAngle3DController>();
        ctrl.OverlayMaterial = mat;
        ctrl.GenerateOnStart = true;

        Camera cam = Camera.main ?? Object.FindObjectOfType<Camera>();
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 0f, -4f);
            cam.orthographic       = true;
            cam.orthographicSize   = 1f;
            cam.backgroundColor    = Color.black;
            cam.clearFlags         = CameraClearFlags.SolidColor;
        }

        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var t = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (t != null) es.AddComponent(t);
            else es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        BuildHUD(ctrlGO, ctrl);

        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(),
            SceneSavePath);
        AssetDatabase.Refresh();
        Selection.activeGameObject = ctrlGO;

        EditorUtility.DisplayDialog("TensionViz",
            "InjectionAngle3DDemo.unity created!\n\n" +
            "Data: synthetic 512x512x49 OCT volume (3 retinal layers)\n\n" +
            "Press Play, then use the on-screen sliders:\n" +
            "  TILT X  = left / right needle tilt\n" +
            "  TILT Y  = forward / back needle tilt\n" +
            "  SLICE Z = depth position in volume\n\n" +
            "Cyan line = surface normal (3D Central Difference)\n" +
            "Arc color = green (safe) / yellow / red (warning)\n" +
            "Red border = deviation > 20 deg",
            "Got it!");
    }

    private static void BuildHUD(GameObject ctrlGO, InjectionAngle3DController ctrl)
    {
        GameObject canvasGO = new GameObject("HUDCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Top-left info panel ───────────────────────────────────────────
        GameObject panel = new GameObject("InfoPanel", typeof(RectTransform));
        panel.transform.SetParent(canvasGO.transform, false);
        panel.AddComponent<Image>().color = new Color(0, 0, 0, 0.60f);
        RectTransform pr = panel.GetComponent<RectTransform>();
        pr.anchorMin = new Vector2(0, 1); pr.anchorMax = new Vector2(0, 1);
        pr.pivot     = new Vector2(0, 1);
        pr.offsetMin = new Vector2(8, -130f);
        pr.offsetMax = new Vector2(310f, -8f);

        InfoLine(panel, 0, "INJECTION ANGLE 3D", Color.white,  15);
        Text deviationLbl = InfoLine(panel, 1, "Deviation:  --",     Color.white, 14);
        Text statusLbl    = InfoLine(panel, 2, "Status:  OK",        Color.green, 14);
        Text normalLbl    = InfoLine(panel, 3, "Normal:  (0,1,0)",   Color.cyan,  12);
        Text needleLbl    = InfoLine(panel, 4, "Needle:  (0,0,-1)",  Color.yellow,12);

        // ── Right-side slider panel ───────────────────────────────────────
        GameObject sliderPanel = new GameObject("SliderPanel", typeof(RectTransform));
        sliderPanel.transform.SetParent(canvasGO.transform, false);
        sliderPanel.AddComponent<Image>().color = new Color(0, 0, 0, 0.65f);
        RectTransform sp = sliderPanel.GetComponent<RectTransform>();
        sp.anchorMin = new Vector2(1, 0.5f); sp.anchorMax = new Vector2(1, 0.5f);
        sp.pivot     = new Vector2(1, 0.5f);
        sp.offsetMin = new Vector2(-260f, -110f);
        sp.offsetMax = new Vector2(-8f,    110f);

        Text tiltXVal  = SliderRow(sliderPanel, "TILT X",  0, -45f, 45f,  0f, out Slider tiltXSlider);
        Text tiltYVal  = SliderRow(sliderPanel, "TILT Y",  1, -45f, 45f,  0f, out Slider tiltYSlider);
        Text sliceZVal = SliderRow(sliderPanel, "SLICE Z", 2,  0f,  1f,  0.5f, out Slider sliceZSlider);

        // ── Wire runtime controller ───────────────────────────────────────
        Angle3DHUDController hud = ctrlGO.AddComponent<Angle3DHUDController>();
        hud.Ctrl         = ctrl;
        hud.DeviationLbl = deviationLbl;
        hud.StatusLbl    = statusLbl;
        hud.NormalLbl    = normalLbl;
        hud.NeedleLbl    = needleLbl;
        hud.TiltXSlider  = tiltXSlider;
        hud.TiltYSlider  = tiltYSlider;
        hud.SliceZSlider = sliceZSlider;
        hud.TiltXVal     = tiltXVal;
        hud.TiltYVal     = tiltYVal;
        hud.SliceZVal    = sliceZVal;
    }

    // ── Creates one labeled slider row, returns value label + slider ──────────

    private static Text SliderRow(GameObject parent, string label,
        int rowIndex, float minV, float maxV, float defaultV,
        out Slider slider)
    {
        float rowH   = 60f;
        float startY = 100f - rowIndex * rowH;

        // Label
        GameObject labelGO = new GameObject(label + "Lbl", typeof(RectTransform));
        labelGO.transform.SetParent(parent.transform, false);
        Text lbl = labelGO.AddComponent<Text>();
        lbl.text      = label;
        lbl.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        lbl.fontSize  = 13;
        lbl.fontStyle = FontStyle.Bold;
        lbl.color     = Color.white;
        lbl.alignment = TextAnchor.MiddleLeft;
        RectTransform lr = labelGO.GetComponent<RectTransform>();
        lr.anchorMin = new Vector2(0, 0.5f); lr.anchorMax = new Vector2(0, 0.5f);
        lr.pivot     = new Vector2(0, 0.5f);
        lr.offsetMin = new Vector2(10f, startY - 12f);
        lr.offsetMax = new Vector2(80f, startY + 12f);

        // Value label
        GameObject valGO = new GameObject(label + "Val", typeof(RectTransform));
        valGO.transform.SetParent(parent.transform, false);
        Text val = valGO.AddComponent<Text>();
        val.text      = $"{defaultV:F1}";
        val.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        val.fontSize  = 13;
        val.fontStyle = FontStyle.Bold;
        val.color     = new Color(0.4f, 0.85f, 1f);
        val.alignment = TextAnchor.MiddleRight;
        RectTransform vr = valGO.GetComponent<RectTransform>();
        vr.anchorMin = new Vector2(1, 0.5f); vr.anchorMax = new Vector2(1, 0.5f);
        vr.pivot     = new Vector2(1, 0.5f);
        vr.offsetMin = new Vector2(-70f, startY - 12f);
        vr.offsetMax = new Vector2(-8f,  startY + 12f);

        // Slider
        GameObject sliderGO = new GameObject(label + "Slider", typeof(RectTransform));
        sliderGO.transform.SetParent(parent.transform, false);
        RectTransform sr = sliderGO.GetComponent<RectTransform>();
        sr.anchorMin = new Vector2(0, 0.5f); sr.anchorMax = new Vector2(1, 0.5f);
        sr.pivot     = new Vector2(0.5f, 0.5f);
        sr.offsetMin = new Vector2(10f,  startY - 36f);
        sr.offsetMax = new Vector2(-10f, startY - 16f);

        slider = sliderGO.AddComponent<Slider>();
        slider.minValue     = minV;
        slider.maxValue     = maxV;
        slider.wholeNumbers = false;
        slider.value        = defaultV;

        // Track
        GameObject bg = new GameObject("Bg", typeof(RectTransform));
        bg.transform.SetParent(sliderGO.transform, false);
        bg.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.2f);
        RectTransform bgr = bg.GetComponent<RectTransform>();
        bgr.anchorMin = new Vector2(0, 0.35f); bgr.anchorMax = new Vector2(1, 0.65f);
        bgr.offsetMin = bgr.offsetMax = Vector2.zero;

        // Fill
        GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGO.transform, false);
        RectTransform far = fillArea.GetComponent<RectTransform>();
        far.anchorMin = new Vector2(0, 0.35f); far.anchorMax = new Vector2(1, 0.65f);
        far.offsetMin = new Vector2(0, 0); far.offsetMax = new Vector2(-10f, 0);

        GameObject fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.6f, 1f);
        RectTransform fr = fill.GetComponent<RectTransform>();
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
        fr.offsetMin = fr.offsetMax = Vector2.zero;
        slider.fillRect = fr;

        // Handle
        GameObject handleArea = new GameObject("HandleArea", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGO.transform, false);
        RectTransform har = handleArea.GetComponent<RectTransform>();
        har.anchorMin = Vector2.zero; har.anchorMax = Vector2.one;
        har.offsetMin = har.offsetMax = Vector2.zero;

        GameObject handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(handleArea.transform, false);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = Color.white;
        RectTransform hr2 = handle.GetComponent<RectTransform>();
        hr2.anchorMin = new Vector2(0, 0.5f); hr2.anchorMax = new Vector2(0, 0.5f);
        hr2.pivot     = new Vector2(0.5f, 0.5f);
        hr2.sizeDelta = new Vector2(24f, 24f);
        slider.handleRect   = hr2;
        slider.targetGraphic = handleImg;

        ColorBlock cb = ColorBlock.defaultColorBlock;
        cb.normalColor      = new Color(0.9f, 0.9f, 1f);
        cb.highlightedColor = new Color(0.4f, 0.85f, 1f);
        cb.pressedColor     = new Color(0.2f, 0.6f,  1f);
        slider.colors = cb;

        return val;
    }

    private static Text InfoLine(GameObject parent, int idx,
        string text, Color col, int size)
    {
        GameObject go = new GameObject($"Line{idx}", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        Text t = go.AddComponent<Text>();
        t.text      = text;
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize  = size;
        t.fontStyle = FontStyle.Bold;
        t.color     = col;
        t.alignment = TextAnchor.MiddleLeft;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0, 1);
        float y = -6f - idx * 24f;
        rt.offsetMin = new Vector2(8,  y - 20f);
        rt.offsetMax = new Vector2(-8, y);
        return t;
    }

    [MenuItem(MenuPath, validate = true)]
    public static bool Validate() => !Application.isPlaying;
}
#endif

// ── Runtime controller ────────────────────────────────────────────────────────

public class Angle3DHUDController : MonoBehaviour
{
    public InjectionAngle3DController Ctrl;

    [Header("Info labels")]
    public UnityEngine.UI.Text DeviationLbl;
    public UnityEngine.UI.Text StatusLbl;
    public UnityEngine.UI.Text NormalLbl;
    public UnityEngine.UI.Text NeedleLbl;

    [Header("Sliders")]
    public UnityEngine.UI.Slider TiltXSlider;
    public UnityEngine.UI.Slider TiltYSlider;
    public UnityEngine.UI.Slider SliceZSlider;

    [Header("Slider value labels")]
    public UnityEngine.UI.Text TiltXVal;
    public UnityEngine.UI.Text TiltYVal;
    public UnityEngine.UI.Text SliceZVal;

    void Start()
    {
        if (TiltXSlider  != null) TiltXSlider.onValueChanged.AddListener(v  => { if (Ctrl) Ctrl.TiltX  = v; });
        if (TiltYSlider  != null) TiltYSlider.onValueChanged.AddListener(v  => { if (Ctrl) Ctrl.TiltY  = v; });
        if (SliceZSlider != null) SliceZSlider.onValueChanged.AddListener(v => { if (Ctrl) Ctrl.SliceZ = v; });
    }

    void Update()
    {
        if (Ctrl == null) return;

        if (TiltXVal  != null) TiltXVal.text  = $"{Ctrl.TiltX:F1} deg";
        if (TiltYVal  != null) TiltYVal.text  = $"{Ctrl.TiltY:F1} deg";
        if (SliceZVal != null) SliceZVal.text  = $"{Ctrl.SliceZ:F2}";

        if (DeviationLbl != null)
            DeviationLbl.text = $"Deviation:  {Ctrl.AngleDeviationDeg:F1} deg";

        if (StatusLbl != null)
        {
            bool w = Ctrl.AngleWarning;
            StatusLbl.text  = w ? "Status:  WARNING" : "Status:  OK";
            StatusLbl.color = w ? new Color(1f, 0.3f, 0.3f) : Color.green;
        }

        if (NormalLbl != null)
        {
            Vector3 n = Ctrl.SurfaceNormal3D;
            NormalLbl.text = $"Normal:  ({n.x:F2}, {n.y:F2}, {n.z:F2})";
        }

        if (NeedleLbl != null)
        {
            Vector3 d = Ctrl.NeedleDir3D;
            NeedleLbl.text = $"Needle:  ({d.x:F2}, {d.y:F2}, {d.z:F2})";
        }
    }
}
