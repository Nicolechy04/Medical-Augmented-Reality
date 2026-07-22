using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

public class ScreenShot : MonoBehaviour
{
    public string screenshotNamePrefix = "screenshot";
    public string savePath = "/screenshots";
    public int width;
    public int height;
    public Camera screenshotCamera;
    public TMP_Dropdown dropdown;
    public KeyCode keyToActivate = KeyCode.F12;
    //Start is called before the first frame update
    public byte[] CaptureScreenshot() 
    {
        screenshotCamera.enabled = true;
        //Create a new render texture with the desired dimensions
        RenderTexture renderTexture = new RenderTexture(width, height, 24);
        screenshotCamera.targetTexture = renderTexture;


        screenshotCamera.Render();
        RenderTexture.active = renderTexture;

        Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenshot.Apply();

        
        //Reset the camera and active texture
        screenshotCamera.targetTexture = null;
        screenshotCamera.enabled = false;
        RenderTexture.active = null;
        Destroy(renderTexture);

        

       

        
        byte[] bytes = screenshot.EncodeToPNG();

        return bytes;
    }

    public void CaptureScreenshotAndSave()
    {
        byte[] bytes = CaptureScreenshot();

        //check if folder exists
        string path = Application.dataPath + savePath;


        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        //   string filename = path + "/" + screenshotNamePrefix + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        string filename = path + "/" + screenshotNamePrefix + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
        File.WriteAllBytes(filename, bytes);

        print("screenshot saved");
    }

    public void CaptureScreenshotAndSave(string path)
    {
        byte[] bytes = CaptureScreenshot();

        File.WriteAllBytes(path, bytes);

        print("screenshot saved");
    }


    // press f12 to save screenshot
    void Update()
    {
        if (Input.GetKeyDown(keyToActivate))
        {
            CaptureScreenshotAndSave();
        }
    }

    private void Awake()
    {
        screenshotCamera.enabled = false;
    }
}
