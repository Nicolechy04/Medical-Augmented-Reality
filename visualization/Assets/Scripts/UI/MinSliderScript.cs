using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class MinSliderScript : MonoBehaviour
{
    
    private Slider slider;
    // public string calculated3DTexturePath;
    // Start is called before the first frame update
    void Start()
    {
        slider = this.GetComponent<Slider>();
        GameObject volumeObject = GameObject.Find("Volume");
        ChangeVolumeTexture volumeTextureChanger = volumeObject != null ? volumeObject.GetComponent<ChangeVolumeTexture>() : null;
        int textureCount = volumeTextureChanger != null ? volumeTextureChanger.GetTexture3DSequenceCount() : 0;
        slider.maxValue = Mathf.Max(0, textureCount - 1);

        slider.value = slider.minValue;
    }
}
