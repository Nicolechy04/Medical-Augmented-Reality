using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GradientFilterHandle : MonoBehaviour
{
    public Camera camera;
    public ComputeShader shader;
    [Tooltip("Will be generated")]
    public RenderTexture result;



    [HideInInspector]
    [Range(0.0f, 50.0f)]
    public float sigma = 0.5f;

    [HideInInspector]
    [Range(0.0f, 1.0f)]
    public float threshold = 0.1f;

    [HideInInspector]
    public float maxthreshold = 1.0f;

    [HideInInspector]
    public bool addFeatureEnhancement = false;
    [HideInInspector]
    public float sigmaFeature = 1.0f;
    [HideInInspector]
    public float contrastFeature = 1.0f;

    [HideInInspector]
    [Header("Only for 2D Filter")]
    [Range(0.0f, 5.0f)]
    public float contrast = 1.0f;

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

    public RenderTexture GradientFilter(RenderTexture tex3D)
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

        //set scale value for for added intensity on edges
        shader.SetFloat("sigma", sigma);
        shader.SetFloat("contrast", contrast);
        shader.SetFloat("maxthreshold", maxthreshold);

        //set values for feature enhancement
        shader.SetBool("addFeatureEnhancement", addFeatureEnhancement);
        shader.SetFloat("sigmaFeature", sigmaFeature);
        shader.SetFloat("contrastFeature", contrastFeature);


        Quaternion volumeRotation = transform.rotation;
        Vector3 viewDirectionVector = camera.transform.forward;
        //rotate the camera forward vector the same as the volume is rotated
        //in order to stil have the same relation between volume gradient direction and viewing direction 

        viewDirectionVector = volumeRotation * viewDirectionVector;
        float[] viewDirection = { viewDirectionVector.x, viewDirectionVector.y, viewDirectionVector.z };
        shader.SetFloats("viewDirection", viewDirection);
        shader.Dispatch(kernelHandle, resolutionX / 8, resolutionY / 8, resolutionZ / 8);

        return result;

    }


    public RenderTexture ThreeDimGradientFilter(RenderTexture tex3D)
    {
        int kernelHandle = 0;
        kernelHandle = shader.FindKernel("ThreeDimGradientFilter");

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

        //set scale value for for added intensity on edges
        shader.SetFloat("sigma", sigma);
        shader.SetFloat("threshold", threshold);
        shader.SetFloat("maxthreshold", maxthreshold);
        shader.SetBool("addFeatureEnhancement", addFeatureEnhancement);

        shader.Dispatch(kernelHandle, resolutionX / 8, resolutionY / 8, resolutionZ / 8);

        return result;

    }
}
