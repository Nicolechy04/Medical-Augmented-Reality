using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SetCropNumber : MonoBehaviour
{
    public GameObject slider;
    private Text text;

    // Start is called before the first frame update
    void Start()
    {
        text = this.GetComponent<Text>();
        UpdateCropNumber();
    }

    public void UpdateCropNumber()
    {
        int sliderValue = (int)slider.GetComponent<Slider>().value;
        text.text = sliderValue.ToString();
    }
}
