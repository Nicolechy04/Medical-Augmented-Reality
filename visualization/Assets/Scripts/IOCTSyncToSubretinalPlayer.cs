using UnityEngine;

// Applies Python's sync clock to the subretinal Unity visualization.
// IOCTOscReceiver stores the latest frame_index from Python, this script tells
// SubretinalSequencePlayer to show that frame.
//
// important: Python is the clock. Unity should NOT autoplay on its own while
// this is enabled or we'd get two timelines fighting each other.
public class IOCTSyncToSubretinalPlayer : MonoBehaviour
{
    [Header("Sync Source")]
    [Tooltip("The receiver listening for Python's OSC messages.")]
    public IOCTOscReceiver receiver;

    [Header("Visualization Target")]
    [Tooltip("The object that actually loads/shows the subretinal OCT sequence.")]
    public SubretinalSequencePlayer player;

    [Header("Behavior")]
    [Tooltip("If on, Python's frame_index controls which frame Unity shows.")]
    public bool syncEnabled = true;

    [Tooltip("If the local Unity player is running, stop it so Python stays the only clock.")]
    public bool stopLocalPlayback = true;

    [Tooltip("Print every frame we apply from Python to the Console (for debugging).")]
    public bool verboseLogging = false;

    private int lastAppliedFrameIndex = -1;

    private void Reset()
    {
        // auto-find on first add, can still override manually in the Inspector
        receiver = FindObjectOfType<IOCTOscReceiver>();
        player = FindObjectOfType<SubretinalSequencePlayer>();
    }

    private void Awake()
    {
        if (receiver == null)
        {
            receiver = FindObjectOfType<IOCTOscReceiver>();
        }

        if (player == null)
        {
            player = FindObjectOfType<SubretinalSequencePlayer>();
        }
    }

    private void Update()
    {
        if (!syncEnabled || receiver == null || player == null)
        {
            return;
        }

        if (!receiver.hasState)
        {
            return;
        }

        // still loading the previous frame, just wait and pick it up next Update()
        if (player.IsLoading)
        {
            return;
        }

        ApplyPythonFrame(receiver.latestFrameIndex);
    }

    private void ApplyPythonFrame(int pythonFrameIndex)
    {
        if (pythonFrameIndex < 0 || pythonFrameIndex == lastAppliedFrameIndex)
        {
            return;
        }

        if (player.TotalFrames <= 0)
        {
            return;
        }

        // Python owns the timing while synced, so stop the local play button
        if (stopLocalPlayback && player.IsPlaying)
        {
            player.TogglePlay();
        }

        int clampedFrameIndex = Mathf.Clamp(
            pythonFrameIndex,
            0,
            player.TotalFrames - 1
        );

        player.GoToFrame(clampedFrameIndex);
        lastAppliedFrameIndex = pythonFrameIndex;

        if (verboseLogging)
        {
            Debug.Log(
                "[IOCT Sync] Applied Python frame "
                + pythonFrameIndex
                + " to SubretinalSequencePlayer frame "
                + clampedFrameIndex
            );
        }
    }
}
