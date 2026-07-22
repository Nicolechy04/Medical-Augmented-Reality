#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

/// <summary>
/// One-click scene builder for the agreed base-visualization layout:
///   • Main panel  – rotatable 3D OCT volume (DVR) with tension heatmap
///   • Bottom-left – fixed top-view thumbnail + OCT crosshair (real-world anchor)
///   • Top-right   – ambient tension gauge (glanceable, camera-independent)
///   • Top-left    – numeric info panel (ILM/RPE distance, cannula tip/start)
///   • Bottom      – playback controls for the 81-frame sequence
///
/// Menu: SubretinalViewer → Setup Base Visualization Scene
/// </summary>
public static class BaseVisualizationSetup
{
    private const string MenuPath       = "SubretinalViewer/Setup Base Visualization Scene";
    private const string SceneSavePath  = "Assets/Scenes/BaseVisualization.unity";
    private const string MatSavePath    = "Assets/Materials/BaseVisualizationDVRMat.mat";
    private const string ShaderName     = "VolumeRendering/DVR_Minimal";
    private const string DataFolderName = "Subretinal Injection 1";

    [MenuItem(MenuPath, priority = 2)]
    public static void Build()
    {
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
            UnityEditor.SceneManagement.NewSceneMode.Single);

        // ── Shader / material ────────────────────────────────────────────
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog("BaseVisualization",
                $"Shader '{ShaderName}' not found.\nMake sure DVR_Minimal.shader is in the project.", "OK");
            return;
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatSavePath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "BaseVisualizationDVRMat" };
            AssetDatabase.CreateAsset(mat, MatSavePath);
            AssetDatabase.SaveAssets();
            mat = AssetDatabase.LoadAssetAtPath<Material>(MatSavePath);
        }
        else
        {
            mat.shader = shader;
        }

        mat.SetFloat("StepSize",          0.005f);
        mat.SetFloat("IsoValue",          0.05f);
        mat.SetFloat("AlphaMultiplier",   80f);
        mat.SetFloat("BP_k_a",            0.3f);
        mat.SetFloat("BP_k_d",            0.6f);
        mat.SetFloat("BlinnPhongTextureX", 512f);
        mat.SetFloat("BlinnPhongTextureY", 512f);
        mat.SetFloat("BlinnPhongTextureZ", 128f);

        // ── Main panel: rotatable OCT volume ─────────────────────────────
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "OCT_Volume";
        cube.transform.position   = Vector3.zero;
        cube.transform.rotation   = Quaternion.identity;
        cube.transform.localScale = new Vector3(1f, 40f / 30f, 1f); // physical aspect: 30x30x40 mm
        cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
        // Keep the BoxCollider (unlike the plain viewer scene) — RotateVolume
        // raycasts against it to know when a drag starts on the volume.
        cube.AddComponent<RotateVolume>();

        // Debug marker: dropped at the computed voxel position of the cannula
        // tip each frame, for calibrating the axis mapping in RealInjectionAngleController.
        // DVR_Minimal uses ZTest Always (required for raymarching), so it ignores
        // depth and paints over anything inside the cube's screen footprint —
        // push the marker's render queue past "Transparent" so it draws last
        // and stays visible instead of getting overpainted every frame.
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "NeedleTipDebugMarker";
        marker.transform.SetParent(cube.transform, false);
        marker.transform.localScale = new Vector3(0.04f, 0.04f / (40f / 30f), 0.04f);
        Object.DestroyImmediate(marker.GetComponent<Collider>());
        Material markerMat = new Material(Shader.Find("Unlit/Color"));
        markerMat.color = Color.magenta;
        markerMat.renderQueue = 3100; // after DVR's "Transparent" (3000) pass
        marker.GetComponent<MeshRenderer>().sharedMaterial = markerMat;

        Camera cam = Camera.main ?? Object.FindObjectOfType<Camera>();
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 0f, -3f);
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic       = false;
            cam.fieldOfView        = 60f;
            cam.backgroundColor    = new Color(0.05f, 0.08f, 0.06f);
            cam.clearFlags         = CameraClearFlags.SolidColor;
        }

        // ── Controller object ─────────────────────────────────────────────
        GameObject ctrl = new GameObject("BaseVisualizationController");

        SubretinalSequencePlayer player = ctrl.AddComponent<SubretinalSequencePlayer>();
        player.VolumeMaterial  = mat;
        player.BscanStep       = 4;
        player.AutoLoadOnStart = true;
        player.AutoPlay        = false;
        player.PlayFPS         = 3f;

        // Leave DataRootPath blank — SubretinalSequencePlayer auto-detects it next to
        // this project's Assets folder at Play time, so the saved scene doesn't carry
        // this machine's absolute path when shared with teammates.
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string dataPath    = Path.Combine(projectRoot, DataFolderName);

        // Tension heatmap — synthetic/animated for now; swap UseSyntheticData=false
        // and call SetDisplacementField()/SetForceBasedTension() once a real
        // tension signal (e.g. from ILM Distance) is wired in.
        TensionHeatmapController heatmap = ctrl.AddComponent<TensionHeatmapController>();
        heatmap.volumeMaterial   = mat;
        heatmap.UseSyntheticData = true;
        heatmap.AnimateSynthetic = true;

        // Injection angle — real tip position + real volume gradient. Axis
        // mapping and safe-range thresholds are provisional; see the script's
        // header comment for how to calibrate.
        RealInjectionAngleController angleCtrl = ctrl.AddComponent<RealInjectionAngleController>();
        angleCtrl.Player      = player;
        angleCtrl.DebugMarker = marker.transform;

        // ── UI ────────────────────────────────────────────────────────────
        BuildUI(player, heatmap, angleCtrl);

        // ── Save scene ────────────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(),
            SceneSavePath);
        AssetDatabase.Refresh();

        Selection.activeGameObject = ctrl;
        EditorUtility.DisplayDialog("BaseVisualization — Done",
            "BaseVisualization.unity created!\n\n" +
            "- Drag in the main view to orbit the OCT volume\n" +
            "- Bottom-left thumbnail = real top view + OCT crosshair (fixed anchor)\n" +
            "- Top-right bars = ambient tension gauge (synthetic demo data for now) + real injection angle gauge\n" +
            "- Magenta dot inside the volume = computed needle-tip voxel position (calibrate RealInjectionAngleController's axis mapping against it)\n" +
            "- Bottom bar = step through the real 81-frame sequence\n\n" +
            (Directory.Exists(dataPath) ? $"Data: {dataPath}" : "Warning: data folder not found, set DataRootPath manually."),
            "Let's go!");
    }

    // ── UI builder ────────────────────────────────────────────────────────────

    static void BuildUI(SubretinalSequencePlayer player, TensionHeatmapController heatmap, RealInjectionAngleController angleCtrl)
    {
        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var newInput = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newInput != null) es.AddComponent(newInput);
            else                  es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        GameObject canvasGO = new GameObject("UICanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Bottom control bar ────────────────────────────────────────────
        GameObject bar = MakePanel(canvasGO, "ControlBar",
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(0, 0), new Vector2(0, 72),
            new Color(0, 0, 0, 0.7f));

        Button prevBtn  = MakeButton(bar, "Prev",      "<",  new Vector2( 12,  8), new Vector2( 68, 64));
        Button playBtn  = MakeButton(bar, "PlayPause", ">",  new Vector2( 76,  8), new Vector2(132, 64));
        Button nextBtn  = MakeButton(bar, "Next",      ">>", new Vector2(140,  8), new Vector2(196, 64));
        Text   frmLabel = MakeLabel (bar, "FrameLabel", "Frame 000 / 000",
                              new Vector2(1, 0), new Vector2(1, 1),
                              new Vector2(-210, 0), new Vector2(-8, 0), 16, TextAnchor.MiddleRight);
        Slider frmSlider = MakeSlider(bar, "FrameSlider",
                              new Vector2(204, 8), new Vector2(-218, -8));

        Text loadingLabel = MakeLabel(canvasGO, "LoadingLabel", "",
            new Vector2(0.5f, 1), new Vector2(0.5f, 1),
            new Vector2(-300, -44), new Vector2(300, -8), 18, TextAnchor.UpperCenter);
        loadingLabel.color = new Color(1f, 0.85f, 0.3f);

        // ── Info panel (top-left) ─────────────────────────────────────────
        GameObject infoPanel = MakePanel(canvasGO, "InfoPanel",
            new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(8, -118), new Vector2(296, -8),
            new Color(0, 0, 0, 0.55f));

        Text infoLabel = MakeLabel(infoPanel, "InfoText", "-",
            Vector2.zero, Vector2.one,
            new Vector2(8, 4), new Vector2(-8, -4), 14, TextAnchor.UpperLeft);
        infoLabel.lineSpacing = 1.3f;

        // ── Top view + crosshair (bottom-left, fixed real-world anchor) ──
        GameObject topViewPanel = MakePanel(canvasGO, "TopViewPanel",
            new Vector2(0, 0), new Vector2(0, 0),
            new Vector2(8, 80), new Vector2(228, 300),
            new Color(0, 0, 0, 0.55f));

        Text topViewCaption = MakeLabel(topViewPanel, "Caption", "top view - real-world anchor",
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(6, -18), new Vector2(-6, -2), 11, TextAnchor.UpperLeft);
        topViewCaption.color = new Color(0.8f, 0.8f, 0.75f);

        GameObject topViewImgGO = new GameObject("TopViewImage", typeof(RectTransform));
        topViewImgGO.transform.SetParent(topViewPanel.transform, false);
        RawImage topViewImg = topViewImgGO.AddComponent<RawImage>();
        RectTransform topViewImgRT = topViewImgGO.GetComponent<RectTransform>();
        topViewImgRT.anchorMin = new Vector2(0, 0);
        topViewImgRT.anchorMax = new Vector2(1, 1);
        topViewImgRT.offsetMin = new Vector2(6, 6);
        topViewImgRT.offsetMax = new Vector2(-6, -20);

        RectTransform crosshair0 = MakeCrosshairLine(topViewImgGO, "CrosshairLine0");
        RectTransform crosshair1 = MakeCrosshairLine(topViewImgGO, "CrosshairLine1");

        // ── Ambient tension gauge (top-right) ─────────────────────────────
        GameObject gaugePanel = MakePanel(canvasGO, "TensionGauge",
            new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-320, -60), new Vector2(-8, -8),
            new Color(0, 0, 0, 0.55f));

        GameObject barBgGO = new GameObject("BarBg", typeof(RectTransform));
        barBgGO.transform.SetParent(gaugePanel.transform, false);
        Image barBg = barBgGO.AddComponent<Image>();
        barBg.color = new Color(0.15f, 0.15f, 0.15f);
        RectTransform barBgRT = barBgGO.GetComponent<RectTransform>();
        barBgRT.anchorMin = new Vector2(0, 0);
        barBgRT.anchorMax = new Vector2(1, 0.5f);
        barBgRT.offsetMin = new Vector2(8, 6);
        barBgRT.offsetMax = new Vector2(-8, -2);

        GameObject barFillGO = new GameObject("BarFill", typeof(RectTransform));
        barFillGO.transform.SetParent(barBgGO.transform, false);
        Image barFill = barFillGO.AddComponent<Image>();
        barFill.color     = new Color(0.39f, 0.60f, 0.13f);
        barFill.type      = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        barFill.fillAmount = 0f;
        RectTransform barFillRT = barFillGO.GetComponent<RectTransform>();
        barFillRT.anchorMin = Vector2.zero;
        barFillRT.anchorMax = Vector2.one;
        barFillRT.offsetMin = barFillRT.offsetMax = Vector2.zero;

        Text gaugeLabel = MakeLabel(gaugePanel, "GaugeLabel", "Tension 0%",
            new Vector2(0, 0.5f), new Vector2(1, 1),
            new Vector2(8, 0), new Vector2(-8, -2), 13, TextAnchor.MiddleLeft);

        TensionGaugeUI gaugeUI = gaugePanel.AddComponent<TensionGaugeUI>();
        gaugeUI.Controller = heatmap;
        gaugeUI.FillImage  = barFill;
        gaugeUI.ValueLabel = gaugeLabel;

        // ── Ambient angle gauge (top-right, below tension) ────────────────
        GameObject anglePanel = MakePanel(canvasGO, "AngleGauge",
            new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-320, -120), new Vector2(-8, -68),
            new Color(0, 0, 0, 0.55f));

        GameObject angleBarBgGO = new GameObject("BarBg", typeof(RectTransform));
        angleBarBgGO.transform.SetParent(anglePanel.transform, false);
        Image angleBarBg = angleBarBgGO.AddComponent<Image>();
        angleBarBg.color = new Color(0.15f, 0.15f, 0.15f);
        RectTransform angleBarBgRT = angleBarBgGO.GetComponent<RectTransform>();
        angleBarBgRT.anchorMin = new Vector2(0, 0);
        angleBarBgRT.anchorMax = new Vector2(1, 0.5f);
        angleBarBgRT.offsetMin = new Vector2(8, 6);
        angleBarBgRT.offsetMax = new Vector2(-8, -2);

        GameObject angleBarFillGO = new GameObject("BarFill", typeof(RectTransform));
        angleBarFillGO.transform.SetParent(angleBarBgGO.transform, false);
        Image angleBarFill = angleBarFillGO.AddComponent<Image>();
        angleBarFill.color      = new Color(0.39f, 0.60f, 0.13f);
        angleBarFill.type       = Image.Type.Filled;
        angleBarFill.fillMethod = Image.FillMethod.Horizontal;
        angleBarFill.fillAmount = 0f;
        RectTransform angleBarFillRT = angleBarFillGO.GetComponent<RectTransform>();
        angleBarFillRT.anchorMin = Vector2.zero;
        angleBarFillRT.anchorMax = Vector2.one;
        angleBarFillRT.offsetMin = angleBarFillRT.offsetMax = Vector2.zero;

        Text angleLabel = MakeLabel(anglePanel, "GaugeLabel", "Angle --",
            new Vector2(0, 0.5f), new Vector2(1, 1),
            new Vector2(8, 0), new Vector2(-8, -2), 13, TextAnchor.MiddleLeft);

        AngleGaugeUI angleGaugeUI = anglePanel.AddComponent<AngleGaugeUI>();
        angleGaugeUI.Player     = player;
        angleGaugeUI.FillImage  = angleBarFill;
        angleGaugeUI.ValueLabel = angleLabel;

        // ── Wire player ───────────────────────────────────────────────────
        player.FrameSlider     = frmSlider;
        player.FrameLabel      = frmLabel;
        player.InfoLabel       = infoLabel;
        player.LoadingLabel    = loadingLabel;
        player.PrevButton      = prevBtn;
        player.NextButton      = nextBtn;
        player.PlayPauseButton = playBtn;
        player.TopViewDisplay  = topViewImg;
        player.CrosshairLine0  = crosshair0;
        player.CrosshairLine1  = crosshair1;
    }

    private static GameObject MakePanel(GameObject parent, string name,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 sizeDelta,
        Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot     = pivot;
        rt.sizeDelta = sizeDelta;
        return go;
    }

    private static Button MakeButton(GameObject parent, string name, string label,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
        Button btn = go.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(0.45f, 0.45f, 0.45f);
        cb.pressedColor     = new Color(0.15f, 0.15f, 0.15f);
        btn.colors = cb;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;

        GameObject textGO = new GameObject("Label");
        textGO.transform.SetParent(go.transform, false);
        Text txt = textGO.AddComponent<Text>();
        txt.text      = label;
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize  = 20;
        txt.fontStyle = FontStyle.Bold;
        txt.color     = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;
        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        return btn;
    }

    private static Button MakeButton(GameObject parent, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        Button btn = MakeButton(parent, name, label, offsetMin, offsetMax);
        RectTransform rt = btn.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        return btn;
    }

    private static Slider MakeSlider(GameObject parent, string name,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;

        Slider slider = go.AddComponent<Slider>();
        slider.minValue     = 0;
        slider.maxValue     = 100;
        slider.wholeNumbers = true;

        GameObject bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(go.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.12f, 0.12f, 0.18f, 1f);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 0.35f);
        bgRT.anchorMax = new Vector2(1f, 0.65f);
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        RectTransform faRT = fillArea.GetComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0f, 0.35f);
        faRT.anchorMax = new Vector2(1f, 0.65f);
        faRT.offsetMin = new Vector2(0f, 0f);
        faRT.offsetMax = new Vector2(-10f, 0f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.65f, 1f, 1f);
        RectTransform fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = fillRT.offsetMax = Vector2.zero;
        slider.fillRect = fillRT;

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        RectTransform haRT = handleArea.GetComponent<RectTransform>();
        haRT.anchorMin = Vector2.zero;
        haRT.anchorMax = Vector2.one;
        haRT.offsetMin = Vector2.zero;
        haRT.offsetMax = Vector2.zero;

        GameObject handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(handleArea.transform, false);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(1f, 1f, 1f, 1f);
        RectTransform handleRT = handle.GetComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0f, 0.5f);
        handleRT.anchorMax = new Vector2(0f, 0.5f);
        handleRT.pivot     = new Vector2(0.5f, 0.5f);
        handleRT.sizeDelta = new Vector2(28f, 28f);
        slider.handleRect  = handleRT;

        ColorBlock cb = ColorBlock.defaultColorBlock;
        cb.normalColor      = new Color(0.9f, 0.9f, 1.0f);
        cb.highlightedColor = new Color(0.4f, 0.85f, 1.0f);
        cb.pressedColor     = new Color(0.2f, 0.6f,  1.0f);
        slider.colors       = cb;
        slider.targetGraphic = handleImg;

        return slider;
    }

    private static Text MakeLabel(GameObject parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax,
        int fontSize, TextAnchor alignment)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Text txt = go.AddComponent<Text>();
        txt.text      = text;
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize  = fontSize;
        txt.fontStyle = FontStyle.Bold;
        txt.color     = Color.white;
        txt.alignment = alignment;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return txt;
    }

    static RectTransform MakeCrosshairLine(GameObject parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.83f, 0.33f, 0.49f, 0.9f); // c-pink 400
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin  = new Vector2(0.5f, 0.5f);
        rt.anchorMax  = new Vector2(0.5f, 0.5f);
        rt.pivot      = new Vector2(0.5f, 0.5f);
        rt.sizeDelta  = new Vector2(10f, 1.5f);
        return rt;
    }

    [MenuItem(MenuPath, validate = true)]
    static bool Validate() => !Application.isPlaying;
}
#endif
