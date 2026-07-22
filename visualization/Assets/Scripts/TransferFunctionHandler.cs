using UnityEngine;
using System.IO;
using System;
using System.Globalization;

public class TransferFunctionHandler : MonoBehaviour
{
    public Material mat;

    [Header("Load Transfer Function")]
    [Tooltip("Name of file without ext. Load on startup if not empty")]
    public string loadFile;
    [Tooltip("Load the transfer function now")]
    public bool loadNow;

    [Header("Save Transfer Function")]
    [Tooltip("Name of file without ext. Save on startup if not empty")]
    public string saveFile;
    [Tooltip("Save the transfer function now")]
    public bool saveNow;

    private CultureInfo ci = new CultureInfo("en-US");

    // Start is called before the first frame update
    void Start()
    {
        CultureInfo.DefaultThreadCurrentCulture = ci; // avoid different float formatting
        if (loadFile != "") loadNow = true;
        if (saveFile != "") saveNow = true;
    }

    // Update is called once per frame
    void Update()
    {
        if (loadNow)
        {
            LoadTransferFunction();
            loadNow = false;
        }
        if (saveNow)
        {
            SaveTransferFunction();
            saveNow = false;
        }
    }

    string[] ReadLinesFromFile(string file)
    {
        string[] lines = new string[21];

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

        return lines;
    }

    Color ParseColor(string line)
    {
        string col_string = line.Split('(')[1].Split(')')[0];
        string[] colors = col_string.Split(',');

        if (colors.Length != 4)
            throw new Exception("Could not parse color from: " + line);

        Color c = new Color();
        for (var j = 0; j < 4; j++)
        {
            c[j] = System.Single.Parse(colors[j], NumberStyles.Float, ci);
        }
        return c;
    }

    void LoadTransferFunction()
    {
        if (loadFile == "")
        {
            Debug.Log("LoadTransferFunction: Specify a transfer function name!");
            return;
        }
        string filePath = "Assets/Resources/TransferFunctions/" + loadFile + ".txt";

        string[] lines;
        try
        {
            lines = ReadLinesFromFile(filePath);
        }
        catch (IOException e)
        {
            Debug.Log("Transfer function file could not be read: " + filePath);
            Debug.Log(e.Message);
            return;
        }

        if (lines.Length == 0)
        {
            Console.WriteLine("Transfer function empty");
            return;
        }
        
        // parse values
        Color[] tfVs = new Color[21];
        for (int i = 0; i <= 20 ; i++)
        {
            try
            {
                tfVs[i] = ParseColor(lines[i]);
            }
            catch (Exception e)
            {
                Debug.Log("Could not parse transfer function: " + e.Message);
                return;
            }
        } 

        // assign values
       // Material mat = this.gameObject.GetComponent<Renderer>().material;
        for (int i = 0; i <= 20 ; i++)
        {
            String id = String.Format("Val{0:d2}", i);
            mat.SetColor(Shader.PropertyToID(id), tfVs[i]);
        }


        Debug.Log("Transfer Function Loaded: " + loadFile);
    }

    void SaveTransferFunction()
    {
        if (saveFile == "")
        {
            Debug.Log("LoadTransferFunction: Specify a transfer function name!");
            return;
        }
        string filePath = "Assets/Resources/TransferFunctions/" + saveFile + ".txt";
        var metaDataFile = File.Create(filePath);

        // write out values
        Material mat = this.gameObject.GetComponent<Renderer>().material;
        StreamWriter writer = new StreamWriter(metaDataFile);
        for (int i = 0; i <= 20 ; i++)
        {
            String id_idx = String.Format("{0:d2}", i);
            Color val = mat.GetColor(Shader.PropertyToID("Val" + id_idx));
            writer.WriteLine("val" + id_idx + ": " + val.ToString());
        }
        writer.Close();

        Debug.Log("Transfer Function Saved: " + saveFile);
    }
}
