using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SliderScript : MonoBehaviour
{
    public float fps;
    private float nextUpdate = 1.0f;
    private bool play = false;

    private Slider slider;
   // public string calculated3DTexturePath;
    // Start is called before the first frame update
    void Start()
    {
        fps = 10;

        slider = GetComponent<Slider>();
        GameObject volumeObject = GameObject.Find("Volume");
        ChangeVolumeTexture volumeTextureChanger = volumeObject != null ? volumeObject.GetComponent<ChangeVolumeTexture>() : null;
        int textureCount = volumeTextureChanger != null ? volumeTextureChanger.GetTexture3DSequenceCount() : 0;
        slider.maxValue = Mathf.Max(0, textureCount - 1);

        //run every 
    }

    
    void Update()
    {
        if (play)
        {
            //move slider according to set fps
            if (Time.time >= nextUpdate)
            {
                nextUpdate = Time.time + 1.0f / fps;
                MoveSlider();
            }
        }
        
    }

    private void MoveSlider() {
        //increase slider value by 1
        if (slider.value + 1 > slider.maxValue)
        {
            //if the end is reach restart at the beginning
            slider.value = 0;
        }
        else {
            slider.value = slider.value + 1;
        }
        
    }

    public void TogglePlay() {
        play = !play;
    }

    public void Rewind()
    {
        slider.value = 0;
    }

    public bool getPlayStatus() {
        return play;
    }
}
