using UnityEngine;

// Bridges Python's per-frame tissue-tension scalar into our visualization.
// Pushes tissue.tension_normalized from IOCTOscReceiver into
// TensionHeatmapController every frame, so the gauge shows the same number
// driving the sound on the Python side.
//
// only overrides the scalar (NormalizedTension/HighTensionAlert) - the
// spatial heatmap still comes from the OCT-difference field until the
// registration team gives us a real displacement field. if Python doesn't
// send a value for a frame we just leave the local one alone.
public class IOCTSyncToTension : MonoBehaviour
{
    [Header("Sync Source")]
    [Tooltip("The receiver listening for Python's OSC messages.")]
    public IOCTOscReceiver receiver;

    [Header("Visualization Target")]
    [Tooltip("The tension controller whose gauge/warning color this drives.")]
    public TensionHeatmapController controller;

    [Header("Behavior")]
    [Tooltip("If on, Python's tension_normalized replaces whatever was computed locally.")]
    public bool usePythonTension = true;

    private void Reset()
    {
        receiver   = FindObjectOfType<IOCTOscReceiver>();
        controller = FindObjectOfType<TensionHeatmapController>();
    }

    private void Awake()
    {
        if (receiver == null)   receiver   = FindObjectOfType<IOCTOscReceiver>();
        if (controller == null) controller = FindObjectOfType<TensionHeatmapController>();
    }

    // LateUpdate so this runs after the local proxy tension is computed, Python wins
    private void LateUpdate()
    {
        if (!usePythonTension || receiver == null || controller == null) return;
        if (!receiver.hasState || !receiver.latestTensionAvailable) return;

        controller.SetExternalTension(receiver.latestTensionNormalized, receiver.latestWarningState);
    }
}
