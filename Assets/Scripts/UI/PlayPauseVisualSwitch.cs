using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlayPauseVisualSwitch : MonoBehaviour
{
    public Slider slider;
    public Sprite playButton;
    public Sprite pauseButton;
    private bool play = false;
    private Image button;
    void Start()
    {
        button = GetComponent<Image>();
        

    }
    public void SwapPlayPauseAppearance()
    {
        //if player is currently playing
        if (slider.GetComponent<SliderScript>().getPlayStatus())
        {
            button.sprite = pauseButton;

        }
        else 
        {
            button.sprite = playButton;
        
        }
            
    }
}
