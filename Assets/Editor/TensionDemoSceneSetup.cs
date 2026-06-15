#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

/// <summary>
/// One-click demo scene setup.
/// Menu: TensionViz → Setup Pig Eye Demo
///
/// Creates a brand-new scene (TensionDemo.unity) containing:
///   • TensionOverlay2D material
///   • Quad with 1000×1024 B-scan aspect ratio
///   • TensionDemoController (TensionHeatmapController + PigEyeSequencePlayer)
///   • Canvas UI: frame slider, Prev / Play-Pause / Next buttons, info labels
///   • Camera (orthographic, framing the quad)
///   • EventSystem (required for UI interaction)
/// </summary>
public static class TensionDemoSceneSetup
{
    private const string MenuPath     = "TensionViz/Setup Pig Eye Demo";
    private const string MatSavePath  = "Assets/Materials/TensionOverlay2DMat.mat";
    private const string ShaderName   = "TensionViz/TensionOverlay2D";
    private const string SceneSavePath = "Assets/Scenes/TensionDemo.unity";

    private static readonly string[] SequenceCandidates =
    {
        Path.Combine("Data","pigeye_samples","no_bounceback","full_seg","06_28_23","b_i3"),
        Path.Combine("Data","pigeye_samples","bounceback","full_seg","06_28_23","b_i14"),
        Path.Combine("Data","pigeye_samples","no_bounceback","full_seg","07_18_23","b_i12"),
    };

    [MenuItem(MenuPath, priority = 1)]
    public static void SetupDemoScene()
    {
        // ── 0. Save current scene → open new scene with Camera + Light ───
        if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
            UnityEditor.SceneManagement.NewSceneMode.Single);

        // ── 1. Shader ────────────────────────────────────────────────────
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            EditorUtility.DisplayDialog("TensionViz Setup",
                $"Shader '{ShaderName}' not found.\n\n" +
                "Make sure Assets/Shaders/TensionOverlay2D.shader exists and Unity has compiled it.",
                "OK");
            return;
        }

        // ── 2. Material ──────────────────────────────────────────────────
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatSavePath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "TensionOverlay2DMat" };
            AssetDatabase.CreateAsset(mat, MatSavePath);
            AssetDatabase.SaveAssets();
            mat = AssetDatabase.LoadAssetAtPath<Material>(MatSavePath);
        }
        else
        {
            mat.shader = shader;
        }

        // ── 3. Quad ──────────────────────────────────────────────────────
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "TensionOverlayQuad";
        quad.transform.position   = Vector3.zero;
        quad.transform.rotation   = Quaternion.identity;
        quad.transform.localScale = new Vector3(1000f / 512f, 1024f / 512f, 1f);
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Object.DestroyImmediate(quad.GetComponent<MeshCollider>());

        // ── 4. Controller object ─────────────────────────────────────────
        GameObject ctrlGO = new GameObject("TensionDemoController");

        TensionHeatmapController heatmap = ctrlGO.AddComponent<TensionHeatmapController>();
        heatmap.UseSyntheticData = false;
        heatmap.AnimateSynthetic = false;
        heatmap.TensionThreshold = 0.35f;
        heatmap.volumeMaterial   = null;

        PigEyeSequencePlayer player = ctrlGO.AddComponent<PigEyeSequencePlayer>();
        player.HeatmapController = heatmap;
        player.OverlayMaterial   = mat;
        player.MaxForceMN        = 5f;
        player.HeatmapSigma      = 0.10f;
        player.AutoPlay          = false;
        player.AutoLoadOnStart   = true;
        player.ShowHUD           = false; // UI replaces OnGUI HUD

        // ── 5. Sequence folder ───────────────────────────────────────────
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string foundFolder = null;
        foreach (string rel in SequenceCandidates)
        {
            string abs = Path.Combine(projectRoot, rel);
            if (Directory.Exists(abs)) { foundFolder = abs; break; }
        }
        if (foundFolder != null)
            player.SequenceFolder = foundFolder;
        else
            Debug.LogWarning("[TensionViz] Pig eye sequence folder not found — set SequenceFolder manually.");

        // ── 6. Camera ────────────────────────────────────────────────────
        Camera cam = Camera.main ?? Object.FindObjectOfType<Camera>();
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 0f, -4f);
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic       = true;
            cam.orthographicSize   = 1024f / 1024f; // quad world-height = 2, half = 1 → fills screen
            cam.backgroundColor    = Color.black;
            cam.clearFlags         = CameraClearFlags.SolidColor;
        }

        // ── 7. UI ────────────────────────────────────────────────────────
        DemoUIController ui = BuildUI(player, heatmap, ctrlGO);

        // ── 8. Save scene ────────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(),
            SceneSavePath);
        AssetDatabase.Refresh();

        Selection.activeGameObject = ctrlGO;

        string msg = "TensionDemo.unity created!\n\n" +
                     "• Press ▶ Play\n" +
                     "• Drag the slider or use ◀ ▶ buttons\n" +
                     "• Frame 52 → heatmap turns yellow/red\n" +
                     "• Frame 102 → peak tension + red border pulse\n\n" +
                     (foundFolder != null
                         ? $"Sequence: {Path.GetFileName(foundFolder)}"
                         : "⚠ Set SequenceFolder in PigEyeSequencePlayer");

        EditorUtility.DisplayDialog("TensionViz — Done", msg, "Let's go!");
    }

    // ── UI builder ────────────────────────────────────────────────────────────

    private static DemoUIController BuildUI(
        PigEyeSequencePlayer player,
        TensionHeatmapController heatmap,
        GameObject ctrlGO)
    {
        // EventSystem (required for button / slider input)
        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();

            // Use InputSystemUIInputModule when the new Input System package is present,
            // otherwise fall back to the legacy StandaloneInputModule.
            var newInputType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newInputType != null)
                es.AddComponent(newInputType);
            else
                es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // Root Canvas
        GameObject canvasGO = new GameObject("DemoCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ── Bottom control bar ────────────────────────────────────────────
        GameObject bottomBar = MakePanel(canvasGO, "BottomBar",
            new Vector2(0f, 0f), new Vector2(1f, 0f),   // stretch horizontally, anchor bottom
            new Vector2(0f, 0f), new Vector2(0f, 80f),  // pivot bottom-left, height 80
            new Color(0f, 0f, 0f, 0.65f));

        RectTransform barRT = bottomBar.GetComponent<RectTransform>();
        barRT.anchorMin  = new Vector2(0f, 0f);
        barRT.anchorMax  = new Vector2(1f, 0f);
        barRT.pivot      = new Vector2(0.5f, 0f);
        barRT.offsetMin  = new Vector2(0f, 0f);
        barRT.offsetMax  = new Vector2(0f, 72f);

        // Prev button
        Button prevBtn = MakeButton(bottomBar, "PrevBtn", "◀",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(16f, 0f), new Vector2(72f, 56f));

        // Play/Pause button
        Button playBtn = MakeButton(bottomBar, "PlayPauseBtn", "▶",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(96f, 0f), new Vector2(152f, 56f));

        // Next button
        Button nextBtn = MakeButton(bottomBar, "NextBtn", "▶▶",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(160f, 0f), new Vector2(222f, 56f));

        // Frame label (right side)
        Text frameLabel = MakeLabel(bottomBar, "FrameLabel", "Frame 000 / 000",
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-220f, -18f), new Vector2(-8f, 18f), 18, TextAnchor.MiddleRight);

        // Frame slider (fills the middle)
        Slider slider = MakeSlider(bottomBar, "FrameSlider",
            new Vector2(230f, 8f), new Vector2(-228f, -8f));

        // ── Speed control bar (bottom-right corner, always visible) ──────
        GameObject speedBar = new GameObject("SpeedBar", typeof(RectTransform));
        speedBar.transform.SetParent(canvasGO.transform, false);
        RectTransform speedBarRT = speedBar.GetComponent<RectTransform>();
        speedBarRT.anchorMin = new Vector2(1f, 0f);
        speedBarRT.anchorMax = new Vector2(1f, 0f);
        speedBarRT.pivot     = new Vector2(1f, 0f);
        speedBarRT.offsetMin = new Vector2(-320f, 80f);
        speedBarRT.offsetMax = new Vector2(0f,   128f);

        Image speedBarBg = speedBar.AddComponent<Image>();
        speedBarBg.color = new Color(0.08f, 0.08f, 0.12f, 0.92f);

        // "SPEED" label
        MakeLabel(speedBar, "SpeedTitle", "SPEED",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(10f, -14f), new Vector2(80f, 14f), 15, TextAnchor.MiddleLeft);

        // FPS value label
        Text speedLabel = MakeLabel(speedBar, "SpeedValueLabel", "5.0 fps",
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-110f, -14f), new Vector2(-8f, 14f), 15, TextAnchor.MiddleRight);
        speedLabel.color = new Color(0.4f, 0.85f, 1f);

        Slider speedSlider = MakeSlider(speedBar, "SpeedSlider",
            new Vector2(84f, 8f), new Vector2(-118f, -8f));

        // ── Info panel (top-left) ─────────────────────────────────────────
        GameObject infoPanel = MakePanel(canvasGO, "InfoPanel",
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 1f), new Vector2(220f, -100f),
            new Color(0f, 0f, 0f, 0.55f));

        RectTransform infoRT = infoPanel.GetComponent<RectTransform>();
        infoRT.anchorMin = new Vector2(0f, 1f);
        infoRT.anchorMax = new Vector2(0f, 1f);
        infoRT.pivot     = new Vector2(0f, 1f);
        infoRT.offsetMin = new Vector2(8f, -104f);
        infoRT.offsetMax = new Vector2(228f, -8f);

        Text forceLabel   = MakeInfoLine(infoPanel, "ForceLabel",   "Force  — mN",    0);
        Text tensionLabel = MakeInfoLine(infoPanel, "TensionLabel", "Tension  0%",     1);
        Text phaseLabel   = MakeInfoLine(infoPanel, "PhaseLabel",   "PreDeformation",  2);

        // ── Wire DemoUIController ─────────────────────────────────────────
        DemoUIController uiCtrl = ctrlGO.AddComponent<DemoUIController>();
        uiCtrl.Player          = player;
        uiCtrl.FrameSlider     = slider;
        uiCtrl.SpeedSlider     = speedSlider;
        uiCtrl.SpeedLabel      = speedLabel;
        uiCtrl.PrevButton      = prevBtn;
        uiCtrl.NextButton      = nextBtn;
        uiCtrl.PlayPauseButton = playBtn;
        uiCtrl.FrameLabel      = frameLabel;
        uiCtrl.ForceLabel      = forceLabel;
        uiCtrl.TensionLabel    = tensionLabel;
        uiCtrl.PhaseLabel      = phaseLabel;
        uiCtrl.PlayPauseLabel  = playBtn.GetComponentInChildren<Text>();

        return uiCtrl;
    }

    // ── UI factory helpers ────────────────────────────────────────────────────

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
        Vector2 anchorMin, Vector2 anchorMax,
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
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
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

        // Track background — rounded dark bar
        GameObject bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(go.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.12f, 0.12f, 0.18f, 1f);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 0.35f);
        bgRT.anchorMax = new Vector2(1f, 0.65f);
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

        // Fill area
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

        // Handle slide area — full height of slider
        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        RectTransform haRT = handleArea.GetComponent<RectTransform>();
        haRT.anchorMin = Vector2.zero;
        haRT.anchorMax = Vector2.one;
        haRT.offsetMin = Vector2.zero;
        haRT.offsetMax = Vector2.zero;

        // Handle — large bright circle so it's easy to grab
        GameObject handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(handleArea.transform, false);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = new Color(1f, 1f, 1f, 1f);
        RectTransform handleRT = handle.GetComponent<RectTransform>();
        handleRT.anchorMin = new Vector2(0f, 0.5f);
        handleRT.anchorMax = new Vector2(0f, 0.5f);
        handleRT.pivot     = new Vector2(0.5f, 0.5f);
        handleRT.sizeDelta = new Vector2(28f, 28f);  // fixed square, clearly visible
        slider.handleRect  = handleRT;

        ColorBlock cb = ColorBlock.defaultColorBlock;
        cb.normalColor      = new Color(0.9f, 0.9f, 1.0f);
        cb.highlightedColor = new Color(0.4f, 0.85f, 1.0f);  // cyan on hover
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

    private static Text MakeInfoLine(GameObject parent, string name, string text, int lineIndex)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        Text txt = go.AddComponent<Text>();
        txt.text      = text;
        txt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize  = 15;
        txt.fontStyle = FontStyle.Bold;
        txt.color     = Color.white;
        txt.alignment = TextAnchor.MiddleLeft;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0f, 1f);
        float y = -6f - lineIndex * 30f;
        rt.offsetMin = new Vector2(8f,  y - 24f);
        rt.offsetMax = new Vector2(-8f, y);
        return txt;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [MenuItem(MenuPath, validate = true)]
    public static bool ValidateSetup() => !Application.isPlaying;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        return go.GetComponent<T>() ?? go.AddComponent<T>();
    }
}
#endif
