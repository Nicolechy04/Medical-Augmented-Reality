using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Angle gauge bar — reads SubretinalSequencePlayer.InjectionAngleDeg
/// (computed per frame from JSON 2D keypoints, no trajectory needed)
/// and drives the fill bar + text label in the UI.
///
/// Color zones (degrees from vertical):
///   GREEN  30°–60°  = safe insertion angle
///   YELLOW 20°–70°  = borderline (caution)
///   RED    outside  = danger
/// </summary>
public class AngleGaugeUI : MonoBehaviour
{
    [Header("Data Source")]
    public SubretinalSequencePlayer Player;

    [Header("UI Elements")]
    public Image FillImage;
    public Text  ValueLabel;

    [Header("Angle Safety Zones")]
    public float GreenMin   = 30f;
    public float GreenMax   = 60f;
    public float YellowBand = 10f;

    [Header("Colors")]
    public Color SafeColor    = new Color(0.39f, 0.80f, 0.13f);
    public Color CautionColor = new Color(0.92f, 0.61f, 0.00f);
    public Color DangerColor  = new Color(0.89f, 0.19f, 0.19f);

    void Update()
    {
        if (Player == null) return;

        float angle = Player.InjectionAngleDeg;   // -1 = no reading

        if (angle < 0f)
        {
            if (ValueLabel != null) ValueLabel.text = "Angle --";
            if (FillImage  != null) FillImage.fillAmount = 0f;
            return;
        }

        // Fill bar: 0° → 0%, 90° → 100%
        if (FillImage != null)
        {
            FillImage.fillAmount = Mathf.Clamp01(angle / 90f);
            FillImage.color      = AngleToColor(angle);
        }

        if (ValueLabel != null)
        {
            string zone = AngleZone(angle);
            ValueLabel.text = $"Angle {angle:F0}° ({zone})";
        }
    }

    Color AngleToColor(float a)
    {
        if (a >= GreenMin && a <= GreenMax) return SafeColor;
        if (a >= GreenMin - YellowBand && a <= GreenMax + YellowBand) return CautionColor;
        return DangerColor;
    }

    string AngleZone(float a)
    {
        if (a >= GreenMin && a <= GreenMax) return "Safe";
        if (a >= GreenMin - YellowBand && a <= GreenMax + YellowBand) return "Caution";
        return "Danger";
    }
}
