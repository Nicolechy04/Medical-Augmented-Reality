using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotateVolume : MonoBehaviour
{
    public bool automaticMotion;

    [Header("Automatic Motion")]
    [Range(1f, 10f)]
    public float automaticSpeed = 5f;

    [Header("Manual Motion")]
    [Range(1f, 14f)]
    public float manualSpeed = 10f;

    private Vector3 _initialMousePos;
    private Vector3 _currentMousePos;

    private bool isCurrentlyDraged = false;

    // Update is called once per frame
    void Update()
    {
        if (automaticMotion)
        {
            gameObject.transform.Rotate(0, automaticSpeed / 50, 0);
            return;
        }

        // Detect drag start/end directly, instead of relying on a Unity
        // EventTrigger — that requires a PhysicsRaycaster + EventSystem to be
        // wired up correctly and can miss the mouse-down frame.
        if (Input.GetMouseButtonDown(0) && HitsThisVolume())
        {
            isCurrentlyDraged = true;
            _initialMousePos  = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            isCurrentlyDraged = false;
        }

        if (isCurrentlyDraged && Input.GetMouseButton(0))
        {
            HandleUnserInput();
        }
    }

    /// <summary>Kept for external callers (e.g. a UI EventTrigger); not required for normal use.</summary>
    public void SetCurrentDragStatus(bool status)
    {
        isCurrentlyDraged = status;
    }

    bool HitsThisVolume()
    {
        Camera cam = Camera.main;
        Collider col = GetComponent<Collider>();
        if (cam == null || col == null) return false;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        return col.Raycast(ray, out _, 1000f);
    }

    void HandleUnserInput()
    {
        _currentMousePos = Input.mousePosition;

        // y mouse translation corresponds to a rotation of the volume around x

        float x_rot = (_currentMousePos.y - _initialMousePos.y);
        x_rot = (Mathf.Abs(x_rot) <= 2f) ? 0f : x_rot;
        x_rot /= (16f - manualSpeed);

        // x mouse translation corresponds to a rotation of teh volume around y

        float y_rot = (_initialMousePos.x - _currentMousePos.x);
        y_rot = (Mathf.Abs(y_rot) <= 2f) ? 0f : y_rot;
        y_rot /= (16f - manualSpeed);

        // apply rotation

        gameObject.transform.RotateAround(new Vector3(0,0,0), new Vector3(1,0,0), x_rot);
        gameObject.transform.RotateAround(new Vector3(0, 0, 0), new Vector3(0, 1, 0), y_rot);

        _initialMousePos = _currentMousePos;
    }
}
