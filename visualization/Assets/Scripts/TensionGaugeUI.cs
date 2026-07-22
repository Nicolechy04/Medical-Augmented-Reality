using UnityEngine;
using UnityEngine.UI;

// Small UI gauge for tension - just reads NormalizedTension from the
// TensionHeatmapController every frame and updates a fill bar + text.
// Separate from the 3D view so you can still see the tension value no
// matter where the camera is looking.
public class TensionGaugeUI : MonoBehaviour
{
    public TensionHeatmapController Controller;
    public Image FillImage;
    public Text  ValueLabel;

    [Header("Colors")]
    public Color LowColor  = new Color(0.39f, 0.60f, 0.13f); // c-green 600
    public Color HighColor = new Color(0.89f, 0.29f, 0.29f); // c-red 600

    void Update()
    {
        if (Controller == null) return;

        float t = Controller.NormalizedTension;

        if (FillImage != null)
        {
            FillImage.fillAmount = t;
            FillImage.color      = Color.Lerp(LowColor, HighColor, t);
        }

        if (ValueLabel != null)
            ValueLabel.text = Controller.HighTensionAlert
                ? $"Tension {t * 100f:F0}% (high)"
                : $"Tension {t * 100f:F0}%";
    }
}
