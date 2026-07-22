using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LimitFPS : MonoBehaviour
{
    public static int FPSCount = 30;
    // Start is called before the first frame update
    void Awake()
    {
        Application.targetFrameRate = FPSCount;
        QualitySettings.vSyncCount = 1; // Limits to monitor refresh rate such that GPU refresh rate is also capped.

    }

    // Update is called once per frame
    void Update()
    {
        Application.targetFrameRate = FPSCount;
    }
}
