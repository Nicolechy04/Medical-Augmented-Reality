using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime glue between the demo Canvas UI and PigEyeSequencePlayer.
/// Created automatically by TensionDemoSceneSetup — no manual wiring needed.
/// </summary>
public class DemoUIController : MonoBehaviour
{
    [Header("Player")]
    public PigEyeSequencePlayer     Player;
    public InjectionAngleController AngleController; // assign in angle scene; null in tension scene

    [Header("Controls")]
    public Slider  FrameSlider;
    public Slider  SpeedSlider;   // maps to Player.PlaybackFPS
    public Button  PrevButton;
    public Button  NextButton;
    public Button  PlayPauseButton;

    [Header("Labels")]
    public Text FrameLabel;
    public Text ForceLabel;
    public Text TensionLabel;
    public Text PhaseLabel;
    public Text PlayPauseLabel;
    public Text SpeedLabel;   // shows current FPS value

    private bool _dragging;

    void Start()
    {
        if (PrevButton      != null) PrevButton.onClick.AddListener(OnPrev);
        if (NextButton      != null) NextButton.onClick.AddListener(OnNext);
        if (PlayPauseButton != null) PlayPauseButton.onClick.AddListener(OnPlayPause);

        if (FrameSlider != null)
        {
            FrameSlider.onValueChanged.AddListener(OnSliderChanged);
            var trigger = FrameSlider.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerDown, _ => _dragging = true);
            AddTrigger(trigger, UnityEngine.EventSystems.EventTriggerType.PointerUp,   _ => _dragging = false);
        }

        if (SpeedSlider != null)
        {
            SpeedSlider.minValue     = 0.5f;
            SpeedSlider.maxValue     = 30f;
            SpeedSlider.wholeNumbers = false;
            SpeedSlider.value        = Player != null ? Player.PlaybackFPS : 5f;
            SpeedSlider.onValueChanged.AddListener(v =>
            {
                if (Player != null) Player.PlaybackFPS = v;
                if (SpeedLabel != null) SpeedLabel.text = $"{v:F1} fps";
            });
        }
    }

    void Update()
    {
        if (Player == null || !Player.IsLoaded) return;

        // Sync slider to player (when not dragging)
        if (!_dragging && FrameSlider != null)
        {
            FrameSlider.minValue = 0;
            FrameSlider.maxValue = Player.TotalFrames - 1;
            FrameSlider.SetValueWithoutNotify(Player.CurrentIndex);
        }

        // Update labels
        PigEyeFrame f = Player.CurrentFrame;

        if (FrameLabel   != null)
            FrameLabel.text = $"Frame  {f.Index:D3} / {Player.TotalFrames - 1:D3}";

        if (AngleController != null)
        {
            // Injection angle mode
            if (ForceLabel != null)
            {
                ForceLabel.text  = $"Deviation  {AngleController.AngleDeviationDeg:F1} deg";
                ForceLabel.color = AngleController.AngleWarning
                    ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.4f);
            }
            if (TensionLabel != null)
            {
                TensionLabel.text  = AngleController.AngleWarning ? "Status  WARNING" : "Status  OK";
                TensionLabel.color = AngleController.AngleWarning
                    ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.4f);
            }
        }
        else
        {
            // Tissue tension mode
            if (ForceLabel != null)
                ForceLabel.text = f.ValidForce
                    ? $"Force  {f.TipForceNorm:F3} mN"
                    : "Force  - (invalid)";

            if (TensionLabel != null)
            {
                float t = Player.HeatmapController != null
                    ? Player.HeatmapController.NormalizedTension : 0f;
                TensionLabel.text  = $"Tension  {t:P0}";
                TensionLabel.color = Color.Lerp(new Color(0.4f, 1f, 0.4f),
                                                new Color(1f, 0.4f, 0.4f), t);
            }
        }

        if (PhaseLabel != null)
        {
            DeformationPhase ph = Player.CurrentPhase;
            PhaseLabel.text = ph.ToString();
            PhaseLabel.color = ph switch
            {
                DeformationPhase.PreDeformation  => new Color(0.4f, 1f, 1f),
                DeformationPhase.Deforming        => Color.yellow,
                DeformationPhase.PeakDeformation  => new Color(1f, 0.4f, 0.4f),
                _                                 => Color.gray
            };
        }

        if (PlayPauseLabel != null)
            PlayPauseLabel.text = Player.AutoPlay ? "⏸" : "▶";
    }

    private void OnPrev()         => Player?.StepBackward();
    private void OnNext()         => Player?.StepForward();
    private void OnPlayPause()    { if (Player != null) Player.AutoPlay = !Player.AutoPlay; }

    private void OnSliderChanged(float val)
    {
        if (_dragging && Player != null)
            Player.GoToFrame(Mathf.RoundToInt(val));
    }

    private static void AddTrigger(
        UnityEngine.EventSystems.EventTrigger trigger,
        UnityEngine.EventSystems.EventTriggerType type,
        UnityEngine.Events.UnityAction<UnityEngine.EventSystems.BaseEventData> action)
    {
        var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }
}
