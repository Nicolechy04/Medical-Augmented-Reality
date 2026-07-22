using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class GaussianBlurHandle : MonoBehaviour
{
    public ComputeShader shader;
    [Tooltip("Will be generated")]
    public RenderTexture result;

   
    

    [HideInInspector]
    [Range(0.0f, 5.0f)]
    public float sigma = 1;

    private Renderer renderer;

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

    public RenderTexture GaussianBlur(RenderTexture tex3D)
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

        //set resolution in each direction
        shader.SetFloat("ResolutionX", resolutionX);
        shader.SetFloat("ResolutionY", resolutionY);
        shader.SetFloat("ResolutionZ", resolutionZ);
            
        shader.SetTexture(kernelHandle, "Tex3D", tex3D);

        //set value for Gaussian Blur
        shader.SetFloat("sigma", sigma);


        shader.Dispatch(kernelHandle, resolutionX / 8, resolutionY / 8, resolutionZ / 8);

        return result;

    }

    //does the same as Gaussian Blur but takes texture3d as input
    public RenderTexture GaussianBlur(Texture3D tex3D)
    {
        int kernelHandle = 0;
        kernelHandle = shader.FindKernel("CSMain");

        if (result == null)

        {
            RenderTextureDescriptor desc = new RenderTextureDescriptor(resolutionX, resolutionY, RenderTextureFormat.ARGB32);
            desc.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            desc.enableRandomWrite = true;
            desc.volumeDepth = resolutionZ;
            result = new RenderTexture(desc);
            result.Create();
        }
        shader.SetTexture(kernelHandle, "Result", result);

        //set resolution in each direction
        shader.SetFloat("ResolutionX", resolutionX);
        shader.SetFloat("ResolutionY", resolutionY);
        shader.SetFloat("ResolutionZ", resolutionZ);

        shader.SetTexture(kernelHandle, "Tex3D", tex3D);

        //set value for Gaussian Blur
        shader.SetFloat("sigma", sigma);


        shader.Dispatch(kernelHandle, resolutionX / 8, resolutionY / 8, resolutionZ / 8);

        return result;
       
    }
}
