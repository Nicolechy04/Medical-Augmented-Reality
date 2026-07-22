using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

using System.IO;
using System.Threading;
using System;

using System.Linq;
using UnityEngine.Rendering;
using System.Text;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;

public class ChangeVolumeTexture : MonoBehaviour
{
 
    private Material volumeMaterial;

    public GameObject enfacePlane;

    private Texture3D texture = null;
    private RenderTexture result = null;
    private Renderer renderer;
    private string currentScanName = "placeholder";
    private List<string> texture3DSequence = new List<string>();


    Texture2D enfaceTexture = null;


    [Header("Volume Resolution Settings")]
    public int resolutionX;
    public int resolutionY;
    public int resolutionZ;

    [Header("Enface Loading Settings")]
    public bool useSingleEnfaceTextureForAllVolumes = false;
    public Texture2D singleEnfaceTexture = null;
    public string enfaceFileName = "video.jpg";
    public string enfaceResourceFolder = "Captures";

    [Header("Texture3D Sequence Loading")]
    public string texture3DResourceFolder = "Calculated3DTextures";

    [Header("Gaussian Filter Settings")]
    public bool gaussianBlurOn = false;
    public float gaussianSigma = 1.0f;

    [Header("Median Filter Settings")]
    public bool medianFilterOn = false;

    [Header("Gradient Filter Settings")]
    public bool gradientFilterOn = false;
    public bool use3DGradientFilter = false;
    public float gradientSigma = 0.5f;
    public float threshold = 0.1f;
    public float maxthreshold = 1.0f;

    [Header("Only for 2D Gradient Filter")]
    public float gradientContrast = 1.0f;

    [Header("Gradient Feature Enhancement Settings")]
    public bool addFeatureEnhancement = false;
    public float gradientSigmaFeature = 1.0f;
    public float gradientContrastFeature = 1.0f;


    private bool medianFilterOnPrevious = false;
    private bool gaussianBlurOnPrevious = false;
    private bool gradientFilterOnPrevious = false;
    private bool addFeatureEnhancementPrevious = false;

    private float gaussianSigmaPrevious = 0.0f;

    private float gradientSigmaPrevious = 0.0f;
    private float thresholdPrevious = 0.0f;
    private float maxthresholdPrevious = 1.0f;
    private float gradientContrastPrevious = 0.0f;
    private float gradientSigmaFeaturePrevious = 1.0f;
    private float gradientContrastFeaturePrevious = 1.0f;

    MedianFilterHandle medianFilter;
    GaussianBlurHandle gaussianBlur;
    GradientFilterHandle gradientFilter;






    private void Awake()
    {
       currentScanName = GetComponent<VolumeFromData>().OctRootFolder;
       volumeMaterial = GameObject.Find("Volume").GetComponent<VolumeFromData>().volumeRenderMaterial;
       RefreshTexture3DSequence();
    }

    private void Update()
    {
        //check if filter status was changed, if so, update the texture
        if (medianFilterOn != medianFilterOnPrevious | gaussianBlurOn != gaussianBlurOnPrevious | gradientFilterOn != gradientFilterOnPrevious | gaussianSigma != gaussianSigmaPrevious | gradientSigma != gradientSigmaPrevious | threshold != thresholdPrevious | maxthreshold != maxthresholdPrevious | gradientContrast != gradientContrastPrevious | addFeatureEnhancement != addFeatureEnhancementPrevious | gradientSigmaFeature != gradientSigmaFeaturePrevious | gradientContrastFeature != gradientContrastFeaturePrevious)
        {
            //update previous values 
            medianFilterOnPrevious = medianFilterOn;
            gaussianBlurOnPrevious = gaussianBlurOn;
            gradientFilterOnPrevious = gradientFilterOn;

            gaussianSigmaPrevious = gaussianSigma;

            thresholdPrevious = threshold;
            gradientSigmaPrevious = gradientSigma;
            maxthresholdPrevious = maxthreshold;
            gradientContrastPrevious = gradientContrast;
            addFeatureEnhancementPrevious = addFeatureEnhancement;
            gradientContrastFeaturePrevious = gradientContrastFeature;
            gradientSigmaFeaturePrevious = gradientSigmaFeature;

            //change values in gaussian filter handle
            gaussianBlur.sigma = gaussianSigma;


            //change values in gradient filter handle
            gradientFilter.sigma = gradientSigma;
            gradientFilter.threshold = threshold;
            gradientFilter.maxthreshold = maxthreshold;
            gradientFilter.contrast = gradientContrast;
            gradientFilter.addFeatureEnhancement = addFeatureEnhancement;
            gradientFilter.sigmaFeature = gradientSigmaFeature;
            gradientFilter.contrastFeature = gradientContrastFeature;



            //recalculate current texture with changed values
            changeVolumeTexture(currentScanName);

        }

       
    }

    private DirectoryInfo[] dirInfo;
    private void Start()
    {
       

        enfacePlane = GameObject.Find("EnfacePlane");
        // read all 3D asset files and store in array
        DirectoryInfo dir = new DirectoryInfo("./Assets/Resources/Captures");
        dirInfo = dir.GetDirectories("*");

        renderer = GetComponent<Renderer>();
        
        medianFilter = GetComponent<MedianFilterHandle>();
        gaussianBlur = GetComponent<GaussianBlurHandle>();
        gradientFilter = GetComponent<GradientFilterHandle>();

      
        changeVolumeTexture(currentScanName);

    }

    public int GetTexture3DSequenceCount()
    {
        RefreshTexture3DSequence();
        return texture3DSequence.Count;
    }

    public static int CountTexture3DAssetsInResourcesFolder(string resourcesFolder)
    {
        return FindTexture3DResourcePaths(resourcesFolder).Count;
    }

    private void RefreshTexture3DSequence()
    {
        texture3DSequence = FindTexture3DResourcePaths(texture3DResourceFolder);
    }

    private static List<string> FindTexture3DResourcePaths(string resourcesFolder)
    {
        string normalizedFolder = NormalizeResourceFolder(resourcesFolder);
        List<string> resourcePaths = new List<string>();

#if UNITY_EDITOR
        string assetFolder = string.IsNullOrEmpty(normalizedFolder)
            ? "Assets/Resources"
            : "Assets/Resources/" + normalizedFolder;

        if (UnityEditor.AssetDatabase.IsValidFolder(assetFolder))
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Texture3D", new[] { assetFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string resourcePath = AssetPathToResourcePath(assetPath);
                if (!string.IsNullOrEmpty(resourcePath))
                {
                    resourcePaths.Add(resourcePath);
                }
            }
        }
#else
        Texture3D[] loadedTextures = Resources.LoadAll<Texture3D>(normalizedFolder);
        for (int i = 0; i < loadedTextures.Length; i++)
        {
            if (loadedTextures[i] != null)
            {
                resourcePaths.Add(JoinResourcePath(normalizedFolder, loadedTextures[i].name));
            }
        }
#endif

        return resourcePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => ExtractFirstNumber(Path.GetFileNameWithoutExtension(path)))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeResourceFolder(string resourcesFolder)
    {
        if (string.IsNullOrWhiteSpace(resourcesFolder))
        {
            return string.Empty;
        }

        return resourcesFolder.Replace('\\', '/').Trim('/');
    }

    private static string JoinResourcePath(string folder, string assetName)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return assetName;
        }

        return folder + "/" + assetName;
    }

    private static string AssetPathToResourcePath(string assetPath)
    {
        const string resourcesMarker = "/Resources/";
        string normalizedPath = assetPath.Replace('\\', '/');
        int resourcesIndex = normalizedPath.IndexOf(resourcesMarker, StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex < 0)
        {
            return string.Empty;
        }

        string resourcePathWithExtension = normalizedPath.Substring(resourcesIndex + resourcesMarker.Length);
        return Path.ChangeExtension(resourcePathWithExtension, null);
    }

    private static int ExtractFirstNumber(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return int.MaxValue;
        }

        StringBuilder digits = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                digits.Append(text[i]);
            }
            else if (digits.Length > 0)
            {
                break;
            }
        }

        if (digits.Length > 0 && int.TryParse(digits.ToString(), out int number))
        {
            return number;
        }

        return int.MaxValue;
    }

   
    //Loads in .exr image via a path and returns a Texture2D with Textureformat RFloat
   public Texture2D LoadEXRImage(string relativeFilePath)
    {
#if UNITY_EDITOR
        // Ensure the path is relative to the Assets folder
        string assetPath = "Assets/" + relativeFilePath;

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (texture == null)
        {
            Debug.LogError("Failed to load .exr file from AssetDatabase: " + assetPath);
            return null;
        }

        Debug.Log("Texture format: " + texture.format.ToString());
        return texture;
#else
        Debug.LogError("AssetDatabase is only available in the editor.");
        return null;
#endif
    }

   


    //returns the index which belongs the capture folder name
    private float getScanNumberFromName(string name) 
    {
        DirectoryInfo dir = new DirectoryInfo("./Assets/Resources/Calculated3DTextures");
        //get all files except .meta files
        FileInfo[] fileInfo = dir.GetFiles("*.*").Where(f => !f.Name.EndsWith(".meta")).ToArray();

        return (float) System.Array.FindIndex(fileInfo, d => d.Name.Equals(name + ".asset"));

    }

    [ContextMenu("Reload")]
    public void Reload() 
    {
        changeVolumeTexture(currentScanName);
    }

    public RenderTexture out3D;
    public void ChangeVolumeTextureBySliderValue(float frameIndex)
    {
        changeVolumeTexture(frameIndex);
    }

    public void changeVolumeTexture(float frameIndex)
    {
        RefreshTexture3DSequence();

        if (texture3DSequence.Count == 0)
        {
            Debug.LogWarning("No Texture3D assets found in Resources folder: " + texture3DResourceFolder);
            return;
        }

        int clampedIndex = Mathf.Clamp(Mathf.RoundToInt(frameIndex), 0, texture3DSequence.Count - 1);
        changeVolumeTexture(texture3DSequence[clampedIndex]);
    }

    public void changeVolumeTexture(string i)
    {
        if (string.IsNullOrWhiteSpace(i))
        {
            Debug.LogWarning("Cannot change volume texture because the requested texture name is empty.");
            return;
        }

        string normalizedInput = Path.ChangeExtension(i.Replace('\\', '/'), null);
        currentScanName = Path.GetFileNameWithoutExtension(normalizedInput);
        //load texture i from assets folder

        string resourcePath = normalizedInput.Contains("/")
            ? normalizedInput
            : JoinResourcePath(NormalizeResourceFolder(texture3DResourceFolder), normalizedInput);

        texture = Resources.Load<Texture3D>(resourcePath);
        if (texture == null)
        {
            Debug.LogWarning("Could not load Texture3D at Resources path: " + resourcePath);
            return;
        }

        UpdateEnfaceTextureForCurrentScan();

        resolutionX = texture.width;
        resolutionY = texture.height;
        resolutionZ = texture.depth;

        // set textures in shader
        renderer.material = volumeMaterial;
        SetVolumeTextureOnMaterial(texture);

        if (result != null)
        {
            result.Release();
        }

        bool filtersEnabled = medianFilterOn || gaussianBlurOn || gradientFilterOn;
        if (!filtersEnabled)
        {
            ApplyVolumeResolutionToMaterial();
            return;
        }

        result = CreateSourceRenderTexture(texture);
        try
        {
            Graphics.CopyTexture(texture, result);
        }
        catch (UnityException ex)
        {
            Debug.LogWarning(
                "Could not copy loaded Texture3D into the filter input RenderTexture. " +
                "Using the raw Texture3D directly for VolumeTex this frame. " +
                "Source format: " + texture.graphicsFormat + ", destination format: " + result.graphicsFormat + ". " +
                ex.Message);
            SetVolumeTextureOnMaterial(texture);
            ApplyVolumeResolutionToMaterial();
            return;
        }

        if (medianFilterOn)
        {
            result = medianFilter.MedianFilter(result);
        }

        if (gaussianBlurOn)
        {
            result = gaussianBlur.GaussianBlur(result);
        }

        if (gradientFilterOn)
        {
            //alternative 3D sobel filter
            if (use3DGradientFilter)
            {
                result = gradientFilter.ThreeDimGradientFilter(result);
            }
            else
            {   // use standard 2D sobel filter
                result = gradientFilter.GradientFilter(result);
            }
        }

        renderer.material = volumeMaterial;
        SetVolumeTextureOnMaterial(result);
        


        ApplyVolumeResolutionToMaterial();


    }

    private void SetVolumeTextureOnMaterial(Texture volumeTexture)
    {
        if (volumeRenderMaterialHasNoVolumeTex())
        {
            Debug.LogWarning("Volume material has no 'VolumeTex' property, so the loaded Texture3D cannot be assigned to the shader.");
            return;
        }

        volumeMaterial.SetTexture("VolumeTex", volumeTexture);
        if (renderer != null && renderer.material != null)
        {
            renderer.material.SetTexture("VolumeTex", volumeTexture);
        }
    }

    private void UpdateEnfaceTextureForCurrentScan()
    {
        if (enfacePlane == null)
        {
            enfacePlane = GameObject.Find("EnfacePlane");
        }

        if (useSingleEnfaceTextureForAllVolumes)
        {
            if (singleEnfaceTexture == null)
            {
                Debug.LogWarning("Use Single Enface Texture For All Volumes is enabled, but no texture is assigned.");
                return;
            }

            ApplyEnfaceTexture(singleEnfaceTexture, "single inspector-assigned enface texture");
            return;
        }

        Texture2D loadedEnfaceTexture = TryLoadEnfaceTexture(currentScanName, out string loadedPath);
        if (loadedEnfaceTexture == null)
        {
            string octRootFolder = GetOctRootFolder();
            if (!string.IsNullOrEmpty(octRootFolder) && !string.Equals(octRootFolder, currentScanName, StringComparison.OrdinalIgnoreCase))
            {
                loadedEnfaceTexture = TryLoadEnfaceTexture(octRootFolder, out loadedPath);
            }
        }

        if (loadedEnfaceTexture == null)
        {
            Debug.LogWarning(
                "Could not load enface texture '" + enfaceFileName + "' from Resources/" +
                NormalizeResourceFolder(enfaceResourceFolder) + " for scan '" + currentScanName +
                "'. Supported extensions: .png, .jpg, .jpeg, .bmp.");
            return;
        }

        ApplyEnfaceTexture(loadedEnfaceTexture, loadedPath);
    }

    private void ApplyEnfaceTexture(Texture2D textureToApply, string sourceDescription)
    {
        enfaceTexture = textureToApply;

        if (enfacePlane == null)
        {
            Debug.LogWarning("Loaded enface texture from " + sourceDescription + ", but no EnfacePlane GameObject was found.");
            return;
        }

        Renderer enfaceRenderer = enfacePlane.GetComponent<Renderer>();
        if (enfaceRenderer == null)
        {
            Debug.LogWarning("Loaded enface texture from " + sourceDescription + ", but EnfacePlane has no Renderer component.");
            return;
        }

        enfaceRenderer.material.SetTexture("_MainTex", enfaceTexture);
    }

    private Texture2D TryLoadEnfaceTexture(string scanName, out string loadedPath)
    {
        loadedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(scanName) || string.IsNullOrWhiteSpace(enfaceFileName))
        {
            return null;
        }

        string resourceFolder = NormalizeResourceFolder(enfaceResourceFolder);
        string captureFolder = JoinResourcePath(resourceFolder, scanName.Trim().Replace('\\', '/').Trim('/'));
        HashSet<string> resourcePathsTried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string candidateFileName in GetEnfaceFileNameCandidates(enfaceFileName))
        {
#if UNITY_EDITOR
            string assetPath = "Assets/Resources/" + JoinResourcePath(captureFolder, candidateFileName);
            Texture2D editorTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (editorTexture != null)
            {
                loadedPath = assetPath;
                return editorTexture;
            }
#endif

            string resourcePath = JoinResourcePath(captureFolder, Path.ChangeExtension(candidateFileName, null));
            if (!resourcePathsTried.Add(resourcePath))
            {
                continue;
            }

            Texture2D resourceTexture = Resources.Load<Texture2D>(resourcePath);
            if (resourceTexture != null)
            {
                loadedPath = "Resources/" + resourcePath;
                return resourceTexture;
            }
        }

        return null;
    }

    private IEnumerable<string> GetEnfaceFileNameCandidates(string fileName)
    {
        string normalizedFileName = fileName.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalizedFileName))
        {
            yield break;
        }

        HashSet<string> yieldedFileNames = new HashSet<string>(StringComparer.Ordinal);
        if (yieldedFileNames.Add(normalizedFileName))
        {
            yield return normalizedFileName;
        }

        string extension = Path.GetExtension(normalizedFileName);
        string fileNameWithoutExtension = string.IsNullOrEmpty(extension)
            ? normalizedFileName
            : normalizedFileName.Substring(0, normalizedFileName.Length - extension.Length);

        string[] supportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };
        for (int i = 0; i < supportedExtensions.Length; i++)
        {
            string candidate = fileNameWithoutExtension + supportedExtensions[i];
            if (yieldedFileNames.Add(candidate))
            {
                yield return candidate;
            }

            candidate = fileNameWithoutExtension + supportedExtensions[i].ToUpperInvariant();
            if (yieldedFileNames.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private string GetOctRootFolder()
    {
        VolumeFromData volumeFromData = GetComponent<VolumeFromData>();
        return volumeFromData != null ? volumeFromData.OctRootFolder : string.Empty;
    }

    private bool volumeRenderMaterialHasNoVolumeTex()
    {
        return volumeMaterial == null || !volumeMaterial.HasProperty("VolumeTex");
    }

    private void ApplyVolumeResolutionToMaterial()
    {
        renderer.material.SetFloat("BlinnPhongTextureX", resolutionX);
        renderer.material.SetFloat("BlinnPhongTextureY", resolutionY);
        renderer.material.SetFloat("BlinnPhongTextureZ", resolutionZ);
    }

    private RenderTexture CreateSourceRenderTexture(Texture3D source)
    {
        RenderTextureDescriptor desc = new RenderTextureDescriptor(source.width, source.height);
        desc.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        desc.volumeDepth = source.depth;
        desc.mipCount = 1;
        desc.enableRandomWrite = false;
        desc.graphicsFormat = source.graphicsFormat;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;

        RenderTexture renderTexture = new RenderTexture(desc);
        renderTexture.wrapMode = TextureWrapMode.Clamp;
        renderTexture.filterMode = source.filterMode;
        renderTexture.Create();
        return renderTexture;
    }


    public static void ColorRenderTexture3D(RenderTexture rt, Color color)
    {
        if (rt == null) throw new System.ArgumentNullException(nameof(rt));
        if (rt.dimension != UnityEngine.Rendering.TextureDimension.Tex3D)
            throw new System.ArgumentException("RenderTexture must have dimension Tex3D.", nameof(rt));

        // Ensure the RT exists
        if (!rt.IsCreated()) rt.Create();

        int w = rt.width;
        int h = rt.height;
        int d = rt.volumeDepth;

        // Pick a CPU texture format that matches the RT as closely as possible.
        // Prefer matching GraphicsFormat (Unity 2019+).
        Texture3D temp3D;
#if UNITY_2019_1_OR_NEWER
        GraphicsFormat gfmt = rt.graphicsFormat != GraphicsFormat.None
            ? rt.graphicsFormat
            : GraphicsFormat.R32G32B32A32_SFloat; // safe fallback for ARGBFloat
        temp3D = new Texture3D(w, h, d, gfmt, TextureCreationFlags.None);
#else
        // Older Unity: fall back to RGBAFloat (pairs with RenderTextureFormat.ARGBFloat)
        temp3D = new Texture3D(w, h, d, TextureFormat.RGBAFloat, false);
#endif

        // Fill all voxels with the chosen color
        var pixels = new Color[w * h * d];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;

        temp3D.SetPixels(pixels);
        temp3D.Apply(false, false);

        // Copy each Z slice from the Texture3D to the RenderTexture 3D
        // Note: CopyTexture requires matching sizes and (usually) matching formats.
        for (int z = 0; z < d; z++)
        {
            // srcElement/dstElement act as the Z slice index for Texture3D/RT3D
            Graphics.CopyTexture(temp3D, z, 0, rt, z, 0);
        }

        // Clean up the temporary asset
        UnityEngine.Object.DestroyImmediate(temp3D);
    }

  

    public void SetMedianFilterOn()
    {
        medianFilterOn = true;
    }

    public void SetMedianFilterOff()
    {
        medianFilterOn = false;
    }

    public void SetGaussianBlurOn()
    {
        gaussianBlurOn = true;
    }

    public void SetGaussianBlurOff()
    {
        gaussianBlurOn = false;
    }

    public void SetGradientFilterOn()
    {
        gradientFilterOn = true;
    }

    public void SetGradientFilterOff()
    {
        gradientFilterOn = false;
    }

    public void SetFeatureEnhancementOn()
    {
        addFeatureEnhancement = true;
    }

    public void SetFeatureEnhancementOff()
    {
        addFeatureEnhancement = false;
    }

    public string GetCurrentScanName() 
    {
        return currentScanName;
    }

    //returns the volume resolution in x y z direction
    public int GetVolumeResolutionX() 
    {
        return resolutionX;
    }

    public int GetVolumeResolutionY()
    {
        return resolutionY;
    }

    public int GetVolumeResolutionZ()
    {
        return resolutionZ;
    }

    public void recalculateVolume() 
    {
        changeVolumeTexture(currentScanName);
    }



}

