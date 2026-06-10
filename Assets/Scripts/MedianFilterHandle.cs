using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class MedianFilterHandle : MonoBehaviour
{

   

    private Renderer renderer;


    public ComputeShader shader;
    [Tooltip("Will be generated")]
    public RenderTexture result;
 
    //contrast adjustment is not currently used
    [HideInInspector]
    [Tooltip("Additional Contrast")]
    public bool contrastAdjustmentOn = false;

    [HideInInspector]
    [Range(0.0f, 100.0f)]
    public float contrastValue = 30.0f;

    [HideInInspector]
    [Range(0.0f, 1.0f)]
    public float contrastMiddleColor = 0.108f;

    private int resolutionX;
    private int resolutionY;
    private int resolutionZ;

    private void Awake()
    {
        ChangeVolumeTexture changeVolume = GetComponent<ChangeVolumeTexture>();
        resolutionX = changeVolume.GetVolumeResolutionX();
        resolutionY = changeVolume.GetVolumeResolutionY();
        resolutionZ = changeVolume.GetVolumeResolutionZ();

       
    }
    public RenderTexture MedianFilter(RenderTexture tex3D)
    {
        int kernelHandle = 0;
        kernelHandle = shader.FindKernel("CSMain");

        if (result == null)

        {
            
            RenderTextureDescriptor desc = new RenderTextureDescriptor(resolutionX, resolutionY, RenderTextureFormat.R8);
            desc.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            desc.enableRandomWrite = true;
            desc.volumeDepth = resolutionZ;
            result = new RenderTexture(desc);
            result.Create();
        }
        shader.SetTexture(kernelHandle, "Result", result);

        shader.SetFloat("ResolutionX", resolutionX);
        shader.SetFloat("ResolutionY", resolutionY);
        shader.SetFloat("ResolutionZ", resolutionZ);
        shader.SetTexture(kernelHandle, "Tex3D", tex3D);

        //contrast adjustment is not currently used
        //set values for contrast adjustment
        shader.SetFloat("ContrastValue", contrastValue);
        shader.SetFloat("ContrastMiddleColor", contrastMiddleColor);
        shader.SetBool("ContrastAdjustmentOn", contrastAdjustmentOn);

        shader.Dispatch(kernelHandle, resolutionX / 8, resolutionY / 8, resolutionZ / 8);


        return result;
    }


}
