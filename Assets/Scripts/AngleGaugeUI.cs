using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ambient gauge for injection angle — mirrors TensionGaugeUI but reads
/// RealInjectionAngleController's 3-state safety classification (Safe /
/// Caution / Danger) instead of a continuous [0,1] value.
/// </summary>
public class AngleGaugeUI : MonoBehaviour
{
    public RealInjectionAngleController Controller;
    public Image FillImage;
    public Text  ValueLabel;

    [Header("Colors")]
    public Color SafeColor    = new Color(0.39f, 0.60f, 0.13f); // c-green 600
    public Color CautionColor = new Color(0.72f, 0.31f, 0.09f); // c-amber 600
    public Color DangerColor  = new Color(0.89f, 0.29f, 0.29f); // c-red 600

    void Update()
    {
        if (Controller == null) return;

        if (!Controller.HasValidReading)
        {
            if (ValueLabel != null) ValueLabel.text = "Angle --";
            if (FillImage  != null) FillImage.fillAmount = 0f;
            return;
        }

        float theta = Controller.AngleFromTangentDeg;

        if (FillImage != null)
        {
            FillImage.fillAmount = Mathf.Clamp01(theta / 90f);
            FillImage.color = Controller.State switch
            {
                RealInjectionAngleController.SafetyState.Safe    => SafeColor,
                RealInjectionAngleController.SafetyState.Caution => CautionColor,
                _                                                 => DangerColor,
            };
        }

        if (ValueLabel != null)
            ValueLabel.text = $"Angle {theta:F0}° ({Controller.State})";
    }
}
