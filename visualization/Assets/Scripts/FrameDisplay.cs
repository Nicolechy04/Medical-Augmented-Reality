using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FrameDisplay : MonoBehaviour
{
    public Slider slider;
    private TMP_Text textObj;
    private float frame = 0.0f;

    // Start is called before the first frame update
    void Start()
    {
        textObj = GetComponent<TMP_Text>();
        UpdateFrameDisplay();

    }


    public void UpdateFrameDisplay()
    {
        //set frame number of display according to slider
        frame = slider.value;

        //also update display
        int tmp = (int)frame;
        textObj.text = tmp.ToString();
    }

    public void IncrementFrame()
    {
        if (slider.value + 1 > slider.maxValue)
        {
            //if the end is reached restart at the beginning
            slider.value = 0;
            frame = 0;
            UpdateFrameDisplay();
        }
        else
        {
            frame += 1.0f;
            slider.value = frame;
            UpdateFrameDisplay();
        }
   

    }

    public void DecrementFrame()
    {
        if (slider.value - 1 < slider.minValue)
        {
            //if the beginning is reached restart at the end
            slider.value = slider.maxValue;
            frame = slider.maxValue;
            UpdateFrameDisplay();
        }
        else
        {
            frame -= 1.0f;
            slider.value = frame;
            UpdateFrameDisplay();
        }


    }

}
