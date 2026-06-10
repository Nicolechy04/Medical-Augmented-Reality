using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Xml;

using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Linq;
using B83.Image.BMP;
public class VolumeFromData : MonoBehaviour
{
    private const string VolumeTextureProperty = "VolumeTex";
    private const string FeatureVolumeTextureProperty = "FeatureVolumeTex";
    private const string ShowFeatureVolumeKeyword = "SHOW_FEATURE_VOLUME";
    private const string FeatureTextureResolutionXProperty = "FeatureTexResX";
    private const string FeatureTextureResolutionYProperty = "FeatureTexResY";
    private const string FeatureTextureResolutionZProperty = "FeatureTexResZ";
    private const TextureFormat RawFeatureTextureFormat = TextureFormat.R16;

    [Header("General Settings")]
    public Material volumeRenderMaterial;

    public List<Texture2D> volumeSlices;
    private Texture3D volume ;
    public Slider slider;
    public enum LoadingSelection { From3DTexture, From2DSlices, FromRawData, FromOCTFolder };
    

    [Header("Loading Options")]
    public LoadingSelection loadFrom = LoadingSelection.FromOCTFolder;
    
    public string OctRootFolder;
    public int numberBscans;
    private Texture2D[] Bscans;

    [Header("Feature Volume Debug")]
    [SerializeField] private string featureSourcePath = string.Empty;
    [SerializeField] private string featureSourceType = string.Empty;
    [SerializeField, TextArea(2, 4)] private string featureLoadStatus = string.Empty;

    [Header("Saving/Calculating Options")]
    public TextureFormat textureFormat = TextureFormat.R8;
    // public string saveAs = "Assets/Resources/Volumes/volume.asset";

    private static bool IsSliceFile(FileInfo file)
    {
        return file.Extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               file.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    private void AssignFeatureVolumeToMaterial(Texture3D featureVolume)
    {
        if (volumeRenderMaterial == null)
        {
            return;
        }

        if (!volumeRenderMaterial.HasProperty(FeatureVolumeTextureProperty))
        {
            if (featureVolume != null)
            {
                Debug.LogWarning("Material has no FeatureVolumeTex property. Feature volume was not assigned.");
            }
            return;
        }

        volumeRenderMaterial.SetTexture(FeatureVolumeTextureProperty, featureVolume);
        if (featureVolume != null)
        {
            featureVolume.wrapMode = TextureWrapMode.Clamp;
            featureVolume.filterMode = FilterMode.Trilinear;
        }

        float featureResX = featureVolume != null ? featureVolume.width : 1.0f;
        float featureResY = featureVolume != null ? featureVolume.height : 1.0f;
        float featureResZ = featureVolume != null ? featureVolume.depth : 1.0f;
        if (volumeRenderMaterial.HasProperty(FeatureTextureResolutionXProperty))
        {
            volumeRenderMaterial.SetFloat(FeatureTextureResolutionXProperty, featureResX);
        }
        if (volumeRenderMaterial.HasProperty(FeatureTextureResolutionYProperty))
        {
            volumeRenderMaterial.SetFloat(FeatureTextureResolutionYProperty, featureResY);
        }
        if (volumeRenderMaterial.HasProperty(FeatureTextureResolutionZProperty))
        {
            volumeRenderMaterial.SetFloat(FeatureTextureResolutionZProperty, featureResZ);
        }

        bool showFeatureVolume = featureVolume != null;
        if (showFeatureVolume)
        {
            volumeRenderMaterial.EnableKeyword(ShowFeatureVolumeKeyword);
        }
        else
        {
            volumeRenderMaterial.DisableKeyword(ShowFeatureVolumeKeyword);
        }

        if (volumeRenderMaterial.HasProperty(ShowFeatureVolumeKeyword))
        {
            volumeRenderMaterial.SetFloat(ShowFeatureVolumeKeyword, showFeatureVolume ? 1.0f : 0.0f);
        }
    }

    private Texture3D CreateVolumeFromSlices(Texture2D[] slices)
    {
        if (slices == null)
        {
            return null;
        }

        List<Texture2D> validSlices = new List<Texture2D>();
        for (int i = 0; i < slices.Length; i++)
        {
            if (slices[i] != null)
            {
                validSlices.Add(slices[i]);
            }
        }

        if (validSlices.Count == 0)
        {
            return null;
        }

        Texture3D output = new Texture3D(validSlices[0].width, validSlices[0].height, validSlices.Count, textureFormat, false);
        output.wrapMode = TextureWrapMode.Clamp;

        Color[] colors = new Color[validSlices[0].width * validSlices[0].height * validSlices.Count];
        for (int i = 0; i < validSlices.Count; i++)
        {
            Color[] slicePixels = validSlices[i].GetPixels();
            System.Array.Copy(slicePixels, 0, colors, validSlices[i].height * validSlices[i].width * i, validSlices[i].height * validSlices[i].width);
        }

        output.SetPixels(colors);
        output.Apply();
        return output;
    }

    private Texture3D TryLoadFeatureVolume(string captureFolder)
    {
        Texture3D featureFromUint16Slices = TryCreateFeatureVolumeFromUint16Slices(captureFolder);
        if (featureFromUint16Slices != null)
        {
            return featureFromUint16Slices;
        }

        string[] featureAssetCandidates = new string[]
        {
            "Calculated3DTextures/" + captureFolder + "_feature",
            "Calculated3DTextures/" + captureFolder + "_Feature",
            "Calculated3DTextures/" + captureFolder + "Feature",
            "Calculated3DTextures/" + captureFolder + "_anomaly",
            "Calculated3DTextures/" + captureFolder + "_Anomaly",
            "Calculated3DTextures/" + captureFolder + "Anomaly",
            "Captures/" + captureFolder + "/feature/feature3D",
            "Captures/" + captureFolder + "/anomaly/anomaly3D",
            "Captures/" + captureFolder + "/segmentation/anomaly3D"
        };

        for (int i = 0; i < featureAssetCandidates.Length; i++)
        {
            Texture3D featureFromAsset = Resources.Load<Texture3D>(featureAssetCandidates[i]);
            if (featureFromAsset != null)
            {
                SetFeatureDebugInfo(
                    "Resources Texture3D Asset",
                    featureAssetCandidates[i],
                    "Loaded feature Texture3D asset from Resources path: " + featureAssetCandidates[i]);
                return featureFromAsset;
            }
        }

        string featureFolder = captureFolder + "_feature";
        string featurePath = "./Assets/Resources/Captures/" + featureFolder;
        if (!Directory.Exists(featurePath))
        {
            featureFolder = captureFolder + "_anomaly";
            featurePath = "./Assets/Resources/Captures/" + featureFolder;
            if (!Directory.Exists(featurePath))
            {
                SetFeatureDebugInfo(
                    "No Feature Source Found",
                    featurePath,
                    "No feature source was found for capture '" + captureFolder + "'. Checked raw uint16 folders, Resources Texture3D assets, and fallback slice folder: " + featurePath);
                return null;
            }
        }

        DirectoryInfo featureDir = new DirectoryInfo(featurePath);
        FileInfo[] featureFiles = featureDir.GetFiles().Where(IsSliceFile).ToArray();
        if (featureFiles.Length == 0)
        {
            SetFeatureDebugInfo(
                "No Valid Feature Slices",
                featurePath,
                "Feature folder exists but contains no .bmp or .png slices: " + featurePath);
            return null;
        }

        Texture2D[] featureSlices = featureFiles[0].Extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            ? LoadBscansFromBMP(featureFolder)
            : LoadBscansFromPNG(featureFolder);

        Texture3D featureFromSlices = CreateVolumeFromSlices(featureSlices);
        if (featureFromSlices != null)
        {
            SetFeatureDebugInfo(
                "Fallback Imported Slice Folder",
                featurePath,
                "Created feature volume from imported slices in folder: " + featurePath);
        }
        else
        {
            SetFeatureDebugInfo(
                "Fallback Imported Slice Folder",
                featurePath,
                "Tried to create feature volume from imported slices but got no valid volume: " + featurePath);
        }

        return featureFromSlices;
    }

    private Texture3D TryCreateFeatureVolumeFromUint16Slices(string captureFolder)
    {
        string capturePath = "./Assets/Resources/Captures/" + captureFolder;
        if (!Directory.Exists(capturePath))
        {
            return null;
        }

        string[] candidateFolders = Directory.GetDirectories(capturePath, "blob_size_uint16", SearchOption.AllDirectories);
        if (candidateFolders == null || candidateFolders.Length == 0)
        {
            return null;
        }

        string selectedFolder = candidateFolders
            .Select(path => new
            {
                Path = path,
                Score = ScoreUint16FeatureFolder(path),
                FileCount = Directory.GetFiles(path, "*.png").Length
            })
            .Where(candidate => candidate.FileCount > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.FileCount)
            .ThenBy(candidate => candidate.Path.Length)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(selectedFolder))
        {
            return null;
        }

        if (UInt16GrayscalePngVolumeLoader.TryCreateTexture3DFromFolder(selectedFolder, RawFeatureTextureFormat, out Texture3D featureVolume, out string logMessage))
        {
            SetFeatureDebugInfo("Raw UInt16 PNG Folder", selectedFolder, logMessage);
            return featureVolume;
        }

        SetFeatureDebugInfo("Raw UInt16 PNG Folder", selectedFolder, logMessage);
        return null;
    }

    private static int ScoreUint16FeatureFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath))
        {
            return 0;
        }

        string normalizedPath = folderPath.Replace('\\', '/');
        int score = 0;

        if (normalizedPath.IndexOf("/anomaly1/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 1000;
        }

        if (normalizedPath.IndexOf("/feature1/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 1000;
        }

        if (normalizedPath.IndexOf("/colored_only/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 100;
        }

        if (normalizedPath.IndexOf("/blob_sizes_out/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score += 10;
        }

        if (normalizedPath.IndexOf("/test_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalizedPath.IndexOf("/_sanity_", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            score -= 100;
        }

        return score;
    }

    private void SetFeatureDebugInfo(string sourceType, string sourcePath, string statusMessage)
    {
        featureSourceType = sourceType ?? string.Empty;
        featureSourcePath = sourcePath ?? string.Empty;
        featureLoadStatus = statusMessage ?? string.Empty;

        if (string.IsNullOrEmpty(statusMessage))
        {
            return;
        }

        string prefix = "[VolumeFromData][FeatureVolume]";
        string formattedMessage = $"{prefix} SourceType='{featureSourceType}' Source='{featureSourcePath}' :: {featureLoadStatus}";

        if (statusMessage.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
            statusMessage.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 ||
            statusMessage.IndexOf("no ", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Debug.LogWarning(formattedMessage);
        }
        else
        {
            Debug.Log(formattedMessage);
        }
    }

    Texture2D[] LoadBscans(string capture)
    {
        Texture2D[] bScans = new Texture2D[numberBscans];

        for (int i = 0; i < numberBscans; i++)
        {
            bScans[i] = Resources.Load<Texture2D>("Captures/" + capture + "/BScans512/" + i.ToString());

        }
        return bScans;
    }

    public Texture2D[] LoadBscansFromBMP(string capture)
    {
        Texture2D[] bScans = new Texture2D[numberBscans];

        for (int i = 0; i < numberBscans; i++)
        {
            //load bmp image
            BMPLoader bmpLoader = new BMPLoader();
            BMPImage bmpImg = bmpLoader.LoadBMP("./Assets/Resources/Captures/" + capture + "/" + i.ToString("000") + ".bmp");

            //convert bmp to texture
            bScans[i] = bmpImg.ToTexture2D();

        }
        return bScans;
    }


    public Texture2D[] LoadBscansFromPNG(string capture)
    {
        Texture2D[] bScans = new Texture2D[numberBscans];
      
        //get all png files in ascending order which has a number as name
        FileInfo[] pngFiles = new DirectoryInfo("./Assets/Resources/Captures/" + capture)
                    .GetFiles("*.png")
                    .OrderBy(f =>
                    {
                        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(f.Name);
                        if (int.TryParse(fileNameWithoutExtension, out int number))
                        {
                            return number;
                        }
                        return int.MaxValue; // place invalid file names at the end of the list
                    })
                    .ToArray();

        int j = 0;
        foreach (FileInfo f in pngFiles)
        {
            // bScans[i] = Resources.Load("Captures/" + capture + "/" + i.ToString()) as Texture2D;
            if (j < numberBscans) {
               
                bScans[j] = Resources.Load("Captures/" + capture + "/" + Path.GetFileNameWithoutExtension(f.Name)) as Texture2D;
                j++;
                
            }
           
        }
      //  print("resolution of loaded bscan: " + bScans[5].width + " " + bScans[5].height + " " + bScans.Length);
        return bScans;
    }

    string[] ReadLinesFromFile(string file)
    {
        string[] lines = new string[21];

        try
        {
            using (StreamReader sr = new StreamReader(file))
            {
                string line;
                // Read and display lines from the file until the end of
                // the file is reached.
                int i = 0;
                while ((line = sr.ReadLine()) != null)
                {
                    lines[i] = line;
                    i++;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Meta data file not found:" + file);
        }

        return lines;
    }

    Vector2 ReadMetaData(string capture)
    {
        string[] lines = ReadLinesFromFile("Assets/Resources/Captures/" + capture + "/MataData.txt");
        Debug.Log(lines);


        float volumeSize = float.Parse((lines[0].Split(':')[1]));

        float volumeDepth = float.Parse((lines[1].Split(':')[1]));

        return new Vector2(volumeSize, volumeDepth);
    }

    Vector2 ReadMetaDataFromXML(string capture)
    {
        XmlDocument doc = new XmlDocument();
        doc.Load("Assets/Resources/Captures/" + capture + "/capture.xml");

        XmlNode node = doc.DocumentElement.SelectSingleNode("/cube/octCaptureInfo/scanHeight");
        float volumeSize = float.Parse(node.InnerText);

        node = doc.DocumentElement.SelectSingleNode("/cube/octCaptureInfo/scanDepth");
        float volumeDepth = float.Parse(node.InnerText);

        return new Vector2(volumeSize, volumeDepth);
    }
    void Start()
    {
        MeshRenderer renderer = GetComponent<MeshRenderer>();

        if (loadFrom == LoadingSelection.From2DSlices)
        {
            Recalculate3DTexturesFromSlices();
        }
    }

    public void Recalculate3DTexturesFromOCTFolder()
    {
        if (loadFrom != LoadingSelection.FromOCTFolder)
        {
            Debug.LogWarning("Loading selection is not set to FromOCTFolder.");
            return;
        }

        DirectoryInfo dir = new DirectoryInfo("./Assets/Resources/Captures");
        DirectoryInfo[] dirInfo = dir.GetDirectories("*");

        Debug.Log("Items in dir: " + dirInfo.Length);

        if (slider != null)
            slider.maxValue = dirInfo.Length - 1;

        DirectoryInfo d = new DirectoryInfo("./Assets/Resources/Captures" + "/" + OctRootFolder);

        Texture2D[] sorted_Bscans = new Texture2D[numberBscans];
        FileInfo[] fileInfo = d.GetFiles().Where(IsSliceFile).ToArray();
        if (fileInfo.Length == 0)
        {
            Debug.LogWarning("No .bmp or .png slices found in folder: " + d.FullName);
            return;
        }

        if (fileInfo[0].Extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            sorted_Bscans = LoadBscansFromBMP(OctRootFolder);
            Bscans = sorted_Bscans;
        }

        if (fileInfo[0].Extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            sorted_Bscans = LoadBscansFromPNG(OctRootFolder);
            Bscans = sorted_Bscans;
        }

        Bscans = sorted_Bscans;

        volume = CreateVolumeFromSlices(sorted_Bscans);
        if (volume != null)
        {
            GetComponent<MeshRenderer>().material = volumeRenderMaterial;
            volumeRenderMaterial.SetTexture(VolumeTextureProperty, volume);

            Texture3D featureVolume = TryLoadFeatureVolume(OctRootFolder);
            AssignFeatureVolumeToMaterial(featureVolume);

            Debug.Log("Colors Loaded");
        }
        else
        {
            Debug.LogWarning("Volume creation failed because no valid source slices were loaded.");
        }
    }

    public void Recalculate3DTexturesFromSlices()
    {
        volume = new Texture3D(volumeSlices[0].width, volumeSlices[0].height, volumeSlices.Count, textureFormat, false);
        volume.wrapMode = TextureWrapMode.Clamp;
        Color32[] colors = new Color32[volumeSlices[0].width * volumeSlices[0].height * volumeSlices.Count];
        for (int i = 0; i < volumeSlices.Count; i++)
        {
            Color32[] slicePixels = volumeSlices[i].GetPixels32();
            System.Array.Copy(slicePixels, 0, colors, volumeSlices[i].height * volumeSlices[i].width * i, volumeSlices[i].height * volumeSlices[i].width);
        }
        if (volume != null)
        {
            volume.SetPixels32(colors);
            volume.Apply();

            GetComponent<MeshRenderer>().material = volumeRenderMaterial;
            volumeRenderMaterial.SetTexture(VolumeTextureProperty, volume);
            AssignFeatureVolumeToMaterial(null);

            Debug.Log("Colors Loaded from 2D slices");
        }
    }

    public void SaveAsset()
    {
#if UNITY_EDITOR
        if (volume == null)
        {
            Debug.LogWarning("No volume to save. Recalculate first.");
            return;
        }

        string path;
        if (loadFrom == LoadingSelection.FromOCTFolder)
            path = "Assets/Resources/Calculated3DTextures/" + OctRootFolder + ".asset";
        else
            path = "Assets/Resources/Volumes/volume.asset";

        UnityEditor.AssetDatabase.CreateAsset(volume, path);
        Debug.Log("Asset saved to: " + path);
#else
        Debug.LogWarning("Saving assets is only supported in the Unity Editor.");
#endif
    }

    void Update()
    {
        GetComponent<MeshRenderer>().material = volumeRenderMaterial;
    }


  




    public Texture2D[] GetBscans()
    {
        return Bscans;
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(VolumeFromData))]
public class VolumeFromDataEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        VolumeFromData script = (VolumeFromData)target;

        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.LabelField("Actions", UnityEditor.EditorStyles.boldLabel);

        if (script.loadFrom == VolumeFromData.LoadingSelection.FromOCTFolder)
        {
            if (GUILayout.Button("Recalculate 3D Textures (OCT Folder)"))
            {
                script.Recalculate3DTexturesFromOCTFolder();
            }
        }

        if (script.loadFrom == VolumeFromData.LoadingSelection.From2DSlices)
        {
            if (GUILayout.Button("Recalculate 3D Textures (2D Slices)"))
            {
                script.Recalculate3DTexturesFromSlices();
            }
        }

        if (GUILayout.Button("Save Asset"))
        {
            script.SaveAsset();
        }
    }
}
#endif
