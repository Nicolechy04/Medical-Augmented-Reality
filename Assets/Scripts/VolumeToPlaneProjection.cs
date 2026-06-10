using System.Collections;
using System.Collections.Generic;
using UnityEngine;



public class VolumeToPlaneProjection : MonoBehaviour
{
    public Color color;
    public int lineWidth = 3;

    public bool drawRectangle = false;
    public bool viewAnimation = false;

    public GameObject animation;
    public GameObject volume;

    private Texture2D texture;
    private int resolutionX;
    private int resolutionY;
    private Texture2D originalTex;

    private float offset = 0.0f;
    private float animationHeight = 1.0f;

    private Vector2 textureCoord1;
    private Vector2 textureCoord2;

    private Renderer renderer;

    //  private Vector2 size;
    // Start is called before the first frame update
    void Start()
    {
        //get renderer and copy current texture;
        renderer = GetComponent<Renderer>();
        try
        {
            originalTex = renderer.material.GetTexture("_MainTex") as Texture2D;
            resolutionX = originalTex.width;
            resolutionY = originalTex.height;

            texture = new Texture2D(resolutionX, resolutionY);
            texture.SetPixels(originalTex.GetPixels());
            texture.Apply();
        }catch (System.Exception e)
        {
            print("Enface Texture not found");
        }
        Vector2 textureCoord1 = new Vector2(0.0f, 0.0f);
        Vector2 textureCoord2 = new Vector2(0.0f, 0.0f);

    }

    // Update is called once per frame
    void Update()
    {
        //explicitly look for drawRectangle == true since we are drawing directly onto the displayed texture, if the texture is changed (during play)
        //the rectangle is also gone, therefore we have to redraw it 
        if (drawRectangle)
        {
            ProjectionOnPlane();
        }
    }

    public void ProjectionOnPlane()
    {

        Vector3 corner1Pos;
        Vector3 corner2Pos;
        Vector3 centerPos;
        corner1Pos = volume.GetComponent<Renderer>().bounds.max;
        corner2Pos = volume.GetComponent<Renderer>().bounds.min;
        centerPos = volume.transform.position;

       
        Ray rayDown;
        RaycastHit hit;

        int layerMask = LayerMask.GetMask("EnfacePlane");

        //reset texture to original state
        texture.SetPixels(originalTex.GetPixels());
        texture.Apply();

        if (drawRectangle)
        {
            rayDown = new Ray(corner1Pos, new Vector3(0, -1, 0));

            //perform two raycast from each corner of the volume onto enface plane
            //these pixel coordinates are then used to draw the rectangle onto the texture
            if (Physics.Raycast(rayDown, out hit, 10.0f,layerMask))
            {
                //get hit coordinates in local transform
                Vector3 hitLocalPosition = gameObject.transform.InverseTransformPoint(hit.point);

                textureCoord1.x = hit.textureCoord.x;
                textureCoord1.y = hit.textureCoord.y;
                textureCoord1.x *= texture.width;
                textureCoord1.y *= texture.height;
            }

            rayDown = new Ray(corner2Pos, new Vector3(0, -1, 0));

            if (Physics.Raycast(rayDown, out hit, 10.0f, layerMask))
            {
                //get hit coordinates in local transform
                Vector3 hitLocalPosition = gameObject.transform.InverseTransformPoint(hit.point);

                textureCoord2.x = hit.textureCoord.x;
                textureCoord2.y = hit.textureCoord.y;
                textureCoord2.x *= texture.width;
                textureCoord2.y *= texture.height;
            }
            print("texCoord1:" + textureCoord1);
            print("texCoord2:" + textureCoord2);

            // DrawRectangle(textureCoord1, textureCoord2, color);
            DrawRectangleFrame(textureCoord1, textureCoord2, color);
        }

        //for placing the animation right we need to use the center of the volume 
        //place the animation where the ray hits and scale it accrodingly to the volume size
        if (viewAnimation)
        {
            rayDown = new Ray(centerPos, new Vector3(0, -1, 0));

            if (Physics.Raycast(rayDown, out hit, 10.0f, layerMask))
            {

                //scale animation accordingly
                //add small offset to avoid clipping
                //set height to a predefined size
                animation.transform.localScale = new Vector3(volume.transform.localScale.x + offset, animationHeight, volume.transform.localScale.z + offset);

                //place animation at hit position
                //move the animation also a bit upwards such that the bottom of the cube shaped animation is touching the plane
                Vector3 hitPosition = hit.point;
                animation.transform.position = hitPosition + new Vector3 (0,animationHeight/2-0.1f,0);

              

                animation.SetActive(true);
            }
        }
        else
        {
            animation.SetActive(false);
        }
       
       
    }

    void DrawRectangle(Vector2 textureCoord1, Vector2 textureCoord2, Color color)
    {
    
        int minX = Mathf.Min((int)textureCoord1.x, (int)textureCoord2.x);
        int maxX = Mathf.Max((int)textureCoord1.x, (int)textureCoord2.x);

        int minY = Mathf.Min((int)textureCoord1.y, (int)textureCoord2.y);
        int maxY = Mathf.Max((int)textureCoord1.y, (int)textureCoord2.y);

       
        for (int x = minX; x < maxX; x++)
        {
            for (int y = minY; y < maxY; y++)
            {
                texture.SetPixel(x, y, color);
            }
        }
        texture.Apply();
        GetComponent<Renderer>().material.mainTexture = texture;
    }

    void DrawRectangleFrame(Vector2 textureCoord1, Vector2 textureCoord2, Color color)
    {
       
        int minX = Mathf.Min((int)textureCoord1.x, (int)textureCoord2.x);
        int maxX = Mathf.Max((int)textureCoord1.x, (int)textureCoord2.x);

        int minY = Mathf.Min((int)textureCoord1.y, (int)textureCoord2.y);
        int maxY = Mathf.Max((int)textureCoord1.y, (int)textureCoord2.y);

        originalTex = renderer.material.GetTexture("_MainTex") as Texture2D;
        texture.SetPixels(originalTex.GetPixels());



        for (int x = minX; x < maxX; x++)
        {
            //draw first horizontal line
            for (int y = minY; y < minY+lineWidth; y++)
            {
                texture.SetPixel(x, y, color);
            }

            //draw second horizontal line
            for (int y = maxY-lineWidth; y < maxY; y++)
            {
                texture.SetPixel(x, y, color);
            }

        }

        //draw vertical lines
        for (int y = minY; y < maxY; y++)
        {
            //draw first vertical line
            for (int x = minX; x < minX + lineWidth; x++)
            {
                texture.SetPixel(x, y, color);
            }

            //draw seocond vertical line
            for (int x = maxX - lineWidth; x < maxX; x++)
            {
                texture.SetPixel(x, y, color);
            }

        }



        texture.Apply();
        GetComponent<Renderer>().material.mainTexture = texture;
    }
     public void SetDrawRectangle (bool i)
    {
        drawRectangle = i;
    } 
    public void SetViewAnimation (bool i)
    {
        viewAnimation = i;
    }
}
