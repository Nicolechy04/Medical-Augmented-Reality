#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

/// <summary>
/// One-click setup for the Injection Angle demo scene.
/// Menu: TensionViz -> Setup Injection Angle Demo
///
/// Creates InjectionAngleDemo.unity with identical UI to TensionDemo:
///   - Bottom bar: Prev / Play-Pause / Next + frame slider + frame label
///   - Speed bar: FPS label + speed slider
///   - Info panel (top-left): Deviation / Status / Phase
///   - InjectionAngleController + PigEyeSequencePlayer wired together
/// </summary>
public static class InjectionAngleDemoSceneSetup
{
    private const string MenuPath      = "TensionViz/Setup Injection Angle Demo";
    private const string MatSavePath   = "Assets/Materials/InjectionAngleOverlayMat.mat";
    private const string ShaderName    = "TensionViz/InjectionAngleOverlay";
    private const string SceneSavePath = "Assets/Scenes/InjectionAngleDemo.unity";

    private static readonly string[] SequenceCandidates =
    {
        Path.Combine("Data","pigeye_samples","no_bounceback","full_seg","06_28_23","b_i3"),
        Path.Combine("Data","pigeye_samples","bounceback","full_seg","06_28_23","b_i14"),
        Path.Combine("Data","pigeye_samples","no_bounceback","full_seg","07_18_23","b_i12"),
    };

    [MenuItem(MenuPath, priority = 2)]
    public static void SetupScene()
    {
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
            UnityEditor.SceneManagement.NewSceneMode.Single);

        // ── Shader & material ─────────────────────────────────────────────
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog("TensionViz",
                $"Shader '{ShaderName}' not found.\nCheck Console for shader errors first.", "OK");
            return;
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatSavePath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "InjectionAngleOverlayMat" };
            AssetDatabase.CreateAsset(mat, MatSavePath);
            AssetDatabase.SaveAssets();
            mat = AssetDatabase.LoadAssetAtPath<Material>(MatSavePath);
        }
        else { mat.shader = shader; }

        // ── Quad ─────────────────────────────────────────────────────────
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "AngleOverlayQuad";
        quad.transform.position   = Vector3.zero;
        quad.transform.rotation   = Quaternion.identity;
        quad.transform.localScale = new Vector3(1000f / 512f, 1024f / 512f, 1f);
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Object.DestroyImmediate(quad.GetComponent<MeshCollider>());

        // ── Controllers ───────────────────────────────────────────────────
        GameObject ctrlGO = new GameObject("AngleDemoController");

        InjectionAngleController angle = ctrlGO.AddComponent<InjectionAngleController>();
        angle.OverlayMaterial    = mat;
        angle.SafeMaxDeg         = 10f;
        angle.WarnMaxDeg         = 20f;
        angle.TangentWindowPx    = 8;
        angle.DirectionSmoothing = 0.25f;

        PigEyeSequencePlayer player = ctrlGO.AddComponent<PigEyeSequencePlayer>();
        player.AngleController   = angle;
        player.HeatmapController = null;
        player.OverlayMaterial   = mat;   // drives _BscanTex each frame
        player.ShowHUD           = false;
        player.AutoPlay          = false;
        player.AutoLoadOnStart   = true;

        // Sequence folder
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string folder = null;
        foreach (string rel in SequenceCandidates)
        {
            string abs = Path.Combine(root, rel);
            if (Directory.Exists(abs)) { folder = abs; break; }
        }
        if (folder != null) player.SequenceFolder = folder;

        // ── Camera ────────────────────────────────────────────────────────
        Camera cam = Camera.main ?? Object.FindObjectOfType<Camera>();
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 0f, -4f);
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic       = true;
            cam.orthographicSize   = 1f;
            cam.backgroundColor    = Color.black;
            cam.clearFlags         = CameraClearFlags.SolidColor;
        }

        // ── UI (identical structure to TensionDemo) ───────────────────────
        BuildUI(player, angle, ctrlGO);

        // ── Save ──────────────────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(),
            SceneSavePath);
        AssetDatabase.Refresh();

        Selection.activeGameObject = ctrlGO;

        EditorUtility.DisplayDialog("TensionViz",
            "InjectionAngleDemo.unity created!\n\n" +
            "- Press Play\n" +
            "- Drag frame slider or use arrow keys\n" +
            "- Cyan line = retinal surface normal\n" +
            "- Colored arc/line = needle angle (green -> red)\n\n" +
            (folder != null ? $"Sequence: {Path.GetFileName(folder)}" : "Set SequenceFolder manually"),
            "Got it!");
    }

    // ── UI builder (mirrors TensionDemoSceneSetup.BuildUI exactly) ────────────

    private static void BuildUI(PigEyeSequencePlayer player,
                                InjectionAngleController angle,
                                GameObject ctrlGO)
    {
        // EventSystem
        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var newInputType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newInputType != null) es.AddComponent(newInputType);
            else es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // Root Canvas
        GameObject canvasGO = new GameObject("DemoCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Bottom control bar ────────────────────────────────────────────
        GameObject bottomBar = MakePanel(canvasGO, "BottomBar", new Color(0f, 0f, 0f, 0.65f));
        RectTransform barRT = bottomBar.GetComponent<RectTransform>();
        barRT.anchorMin = new Vector2(0f, 0f);
        barRT.anchorMax = new Vector2(1f, 0f);
        barRT.pivot     = new Vector2(0.5f, 0f);
        barRT.offsetMin = new Vector2(0f, 0f);
        barRT.offsetMax = new Vector2(0f, 72f);

        Button prevBtn = MakeButton(bottomBar, "PrevBtn", "◀",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(16f, 0f), new Vector2(72f, 56f));

        Button playBtn = MakeButton(bottomBar, "PlayPauseBtn", "▶",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(96f, 0f), new Vector2(152f, 56f));

        Button nextBtn = MakeButton(bottomBar, "NextBtn", "▶▶",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(160f, 0f), new Vector2(222f, 56f));

        Text frameLabel = MakeLabel(bottomBar, "FrameLabel", "Frame 000 / 000",
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-220f, -18f), new Vector2(-8f, 18f), 18, TextAnchor.MiddleRight);

        Slider frameSlider = MakeSlider(bottomBar, "FrameSlider",
            new Vector2(230f, 8f), new Vector2(-228f, -8f));

        // ── Speed bar (bottom-right) ──────────────────────────────────────
        GameObject speedBar = new GameObject("SpeedBar", typeof(RectTransform));
        speedBar.transform.SetParent(canvasGO.transform, false);
        RectTransform speedBarRT = speedBar.GetComponent<RectTransform>();
        speedBarRT.anchorMin = new Vector2(1f, 0f);
        speedBarRT.anchorMax = new Vector2(1f, 0f);
        speedBarRT.pivot     = new Vector2(1f, 0f);
        speedBarRT.offsetMin = new Vector2(-320f, 80f);
        speedBarRT.offsetMax = new Vector2(0f,   128f);
        speedBar.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.12f, 0.92f);

        MakeLabel(speedBar, "SpeedTitle", "SPEED",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(10f, -14f), new Vector2(80f, 14f), 15, TextAnchor.MiddleLeft);

        Text speedLabel = MakeLabel(speedBar, "SpeedValueLabel", "5.0 fps",
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-110f, -14f), new Vector2(-8f, 14f), 15, TextAnchor.MiddleRight);
        speedLabel.color = new Color(0.4f, 0.85f, 1f);

        Slider speedSlider = MakeSlider(speedBar, "SpeedSlider",
            new Vector2(84f, 8f), new Vector2(-118f, -8f));

        // ── Wire DemoUIController (no info panel — arc is the only visual indicator) ──
        DemoUIController uiCtrl = ctrlGO.AddComponent<DemoUIController>();
        uiCtrl.Player          = player;
        uiCtrl.AngleController = angle;
        uiCtrl.FrameSlider     = frameSlider;
        uiCtrl.SpeedSlider     = speedSlider;
        uiCtrl.SpeedLabel      = speedLabel;
        uiCtrl.PrevButton      = prevBtn;
        uiCtrl.NextButton      = nextBtn;
        uiCtrl.PlayPauseButton = playBtn;
        uiCtrl.FrameLabel      = frameLabel;
        uiCtrl.PlayPauseLabel  = playBtn.GetComponentInChildren<Text>();
        // ForceLabel / TensionLabel / PhaseLabel intentionally left null — no numeric overlay
    }

    // ── UI factory helpers (identical to TensionDemoSceneSetup) ──────────────

    private static GameObject MakePanel(GameObject parent, string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<Image>().color = color;
        go.AddComponent<RectTransform>();
        return go;
    }

    private static Button MakeButton(GameObject parent, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
        Button btn = go.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(0.45f, 0.45f, 0.45f);
        cb.pressedColor     = new Color(0.15f, 0.15f, 0.15f);
        btn.colors = cb;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;

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
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
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
        slider.minValue = 0; slider.maxValue = 100; slider.wholeNumbers = true;

        GameObject bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(go.transform, false);
        bg.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 1f);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 0.35f); bgRT.anchorMax = new Vector2(1f, 0.65f);
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        RectTransform faRT = fillArea.GetComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0f, 0.35f); faRT.anchorMax = new Vector2(1f, 0.65f);
        faRT.offsetMin = new Vector2(0f, 0f); faRT.offsetMax = new Vector2(-10f, 0f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.65f, 1f, 1f);
        RectTransform fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = fillRT.offsetMax = Vector2.zero;
        slider.fillRect = fillRT;

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        RectTransform haRT = handleArea.GetComponent<RectTransform>();
        haRT.anchorMin = Vector2.zero; haRT.anchorMax = Vector2.one;
        haRT.offsetMin = haRT.offsetMax = Vector2.zero;

        GameObject handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(handleArea.transform, false);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(1f, 1f, 1f, 1f);
        RectTransform handleRT = handle.GetComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0f, 0.5f); handleRT.anchorMax = new Vector2(0f, 0.5f);
        handleRT.pivot     = new Vector2(0.5f, 0.5f);
        handleRT.sizeDelta = new Vector2(28f, 28f);
        slider.handleRect  = handleRT;

        ColorBlock cb = ColorBlock.defaultColorBlock;
        cb.normalColor      = new Color(0.9f, 0.9f, 1.0f);
        cb.highlightedColor = new Color(0.4f, 0.85f, 1.0f);
        cb.pressedColor     = new Color(0.2f, 0.6f,  1.0f);
        slider.colors        = cb;
        slider.targetGraphic = handleImg;
        return slider;
    }

    private static Text MakeLabel(GameObject parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
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
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        return txt;
    }

[MenuItem(MenuPath, validate = true)]
    public static bool Validate() => !Application.isPlaying;
}
#endif
