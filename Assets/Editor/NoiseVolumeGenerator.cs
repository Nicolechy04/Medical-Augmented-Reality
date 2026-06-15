using UnityEngine;
using UnityEditor;
using System.IO;

// Editor tool: generates a tileable 3D grain-noise Texture3D for the
// Tension "Flow Advection" overlay (Initial Project Set Up, p.11).
// Tools > Visualization > Generate Noise Volume
public class NoiseVolumeGenerator : EditorWindow
{
    private int resolution = 64;
    private int octaves = 4;
    private float baseFrequency = 4f;
    private float persistence = 0.5f;
    private string assetName = "TensionGrainNoise";

    [MenuItem("Tools/Visualization/Generate Noise Volume")]
    public static void ShowWindow()
    {
        GetWindow<NoiseVolumeGenerator>("Noise Volume Generator");
    }

    void OnGUI()
    {
        GUILayout.Label("3D Grain Noise Texture (Tension Flow)", EditorStyles.boldLabel);
        resolution = EditorGUILayout.IntField("Resolution (cube)", resolution);
        octaves = EditorGUILayout.IntField("Octaves", octaves);
        baseFrequency = EditorGUILayout.FloatField("Base Frequency", baseFrequency);
        persistence = EditorGUILayout.Slider("Persistence", persistence, 0f, 1f);
        assetName = EditorGUILayout.TextField("Asset Name", assetName);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Generates a grayscale (R8) Texture3D with wrapMode=Repeat so it can be " +
            "scrolled/offset in the shader (advection) without seams.\n" +
            "Saved to Assets/Resources/Calculated3DTextures/<name>.asset",
            MessageType.Info);

        if (GUILayout.Button("Generate & Save"))
        {
            GenerateAndSave();
        }
    }

    private void GenerateAndSave()
    {
        Texture3D tex = Generate3DNoise(resolution, octaves, baseFrequency, persistence);

        string folder = "Assets/Resources/Calculated3DTextures";
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        string path = $"{folder}/{assetName}.asset";
        AssetDatabase.CreateAsset(tex, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Saved noise volume to {path} ({resolution}^3, {octaves} octaves)");
        EditorGUIUtility.PingObject(tex);
    }

    public static Texture3D Generate3DNoise(int resolution, int octaves, float baseFrequency, float persistence)
    {
        Texture3D texture = new Texture3D(resolution, resolution, resolution, TextureFormat.R8, false);
        texture.wrapMode = TextureWrapMode.Repeat; // tileable for advection scrolling
        texture.filterMode = FilterMode.Bilinear;

        Color[] colors = new Color[resolution * resolution * resolution];

        // Random per-octave offsets avoid axis-aligned artifacts from Mathf.PerlinNoise
        Vector3[] offsets = new Vector3[octaves];
        var rng = new System.Random(12345); // fixed seed -> reproducible texture
        for (int o = 0; o < octaves; o++)
        {
            offsets[o] = new Vector3(
                (float)rng.NextDouble() * 1000f,
                (float)rng.NextDouble() * 1000f,
                (float)rng.NextDouble() * 1000f);
        }

        for (int z = 0; z < resolution; z++)
        {
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float value = 0f;
                    float amplitude = 1f;
                    float frequency = baseFrequency;
                    float maxAmplitude = 0f;

                    for (int o = 0; o < octaves; o++)
                    {
                        float nx = (x / (float)resolution) * frequency + offsets[o].x;
                        float ny = (y / (float)resolution) * frequency + offsets[o].y;
                        float nz = (z / (float)resolution) * frequency + offsets[o].z;

                        value += Sample3DPerlin(nx, ny, nz) * amplitude;
                        maxAmplitude += amplitude;

                        amplitude *= persistence;
                        frequency *= 2f;
                    }

                    value /= maxAmplitude; // normalize back to ~0..1
                    int index = x + y * resolution + z * resolution * resolution;
                    colors[index] = new Color(value, value, value, 1f);
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        return texture;
    }

    // Approximate 3D Perlin noise by combining three orthogonal 2D samples.
    private static float Sample3DPerlin(float x, float y, float z)
    {
        float xy = Mathf.PerlinNoise(x, y);
        float yz = Mathf.PerlinNoise(y, z);
        float xz = Mathf.PerlinNoise(x, z);

        float yx = Mathf.PerlinNoise(y, x);
        float zy = Mathf.PerlinNoise(z, y);
        float zx = Mathf.PerlinNoise(z, x);

        return (xy + yz + xz + yx + zy + zx) / 6f;
    }
}
