using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class FPSDisplay : MonoBehaviour
{
    public Slider slider;
    private Text textObj;
    private float fps = 10.0f;

    // Start is called before the first frame update
    void Start()
    {
        textObj = GetComponent<Text>(); 
        UpdateFPS();

    }

    
    void UpdateFPS() {
        //set fps of the slider according to display
        slider.GetComponent<SliderScript>().fps = fps;

        //also update display
        int tmp = (int)fps;
        textObj.text = tmp.ToString();
    }

    public void IncrementFPS() {
       fps +=1.0f;
       UpdateFPS();
    
    }

    public void DecrementFPS()
    {
        fps -= 1.0f;
        UpdateFPS();

    }

}
