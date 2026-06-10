using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class UISwitchHandler : MonoBehaviour
{
    private bool buttonOn = false;
    public bool startingState = false;
    public Button button;
    public RawImage background;
    public Color colorOn;
    public Color colorOff;
    public UnityEvent on;
    public UnityEvent off;

    public void Start()
    {
        buttonOn = startingState;
        UpdateButton();
        
    }


    public void OnSwitchClicked() 
    {
        //toggle state
        buttonOn = !buttonOn;
        UpdateButton();
        if (buttonOn)
        {
            on.Invoke();
        }
        else 
        {
            off.Invoke();
        }

    }

    public void UpdateButton() 
    {
        if (buttonOn) 
        {
            button.transform.localPosition = new Vector3 (50,0,0);
            background.color = colorOn;

        }
        else
        {
            button.transform.localPosition= new Vector3(-50, 0, 0);
            background.color = colorOff;
        }
    }

    public void ButtonOff()
    {
        off.Invoke();
        buttonOn = false;
        UpdateButton();
    }

    public void ButtonOn()
    {
        on.Invoke();
        buttonOn = true;
        UpdateButton();
    }
}
