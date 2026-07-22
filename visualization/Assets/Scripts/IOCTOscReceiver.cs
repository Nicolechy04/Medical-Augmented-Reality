using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Receives sync messages from the Python sonification loop over UDP.
// Python owns the clock - Unity just shows whatever frame_index it sends.
//
// only implements the tiny subset of OSC that sync_sender.py actually uses
// (address string, type tag ",s", one JSON string arg) so we don't need an
// external OSC package for this one use case.
public class IOCTOscReceiver : MonoBehaviour
{
    [Header("Network")]
    [Tooltip("UDP port that synchronization/sync_sender.py sends to.")]
    public int port = 12002;

    [Tooltip("Turn on to print every received frame message to the Console (useful for debugging).")]
    public bool verboseLogging = false;

    [Header("Latest Python State")]
    [Tooltip("Becomes true once we've gotten at least one /ioct/state_json message.")]
    public bool hasState = false;

    [Tooltip("True for exactly one Unity frame whenever a new Python frame comes in.")]
    public bool hasNewState = false;

    public string currentCaptureName = "";
    public int latestFrameIndex = -1;
    public float latestTimestampSeconds = 0.0f;
    public string latestWarningState = "unknown";

    public Vector3 latestNeedleTip = Vector3.zero;
    public bool latestNeedleTipAvailable = false;
    public float latestInjectionDepth = 0.0f;
    public bool latestInjectionDepthAvailable = false;
    public float latestTensionNormalized = 0.0f;
    public bool latestTensionAvailable = false;

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool running = false;

    // Unity stuff can only be touched from the main thread - UDP thread stores it here
    private readonly object payloadLock = new object();
    private string pendingAddress = "";
    private string pendingJson = "";
    private bool pendingMessage = false;

    private void OnEnable()
    {
        StartListening();
    }

    private void OnDisable()
    {
        StopListening();
    }

    private void Update()
    {
        string address = "";
        string json = "";
        bool received = false;

        lock (payloadLock)
        {
            if (pendingMessage)
            {
                address = pendingAddress;
                json = pendingJson;
                pendingAddress = "";
                pendingJson = "";
                pendingMessage = false;
                received = true;
            }
        }

        // one-frame pulse, so IOCTSyncToSlider only applies on an actual new tick
        hasNewState = false;

        if (!received)
        {
            return;
        }

        if (address == "/ioct/state_json")
        {
            ApplyStateJson(json);
        }
        else if (address == "/ioct/end")
        {
            if (verboseLogging)
            {
                Debug.Log("[IOCT OSC] Received end message: " + json);
            }
        }
        else if (address == "/ioct/capture_ready")
        {
            if (verboseLogging)
            {
                Debug.Log("[IOCT OSC] Received capture_ready message: " + json);
            }
        }
        else if (verboseLogging)
        {
            Debug.Log("[IOCT OSC] Ignored address: " + address);
        }
    }

    private void StartListening()
    {
        if (running)
        {
            return;
        }

        try
        {
            udpClient = new UdpClient(port);
            running = true;
            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log("[IOCT OSC] Listening on UDP port " + port);
        }
        catch (Exception ex)
        {
            running = false;
            Debug.LogError("[IOCT OSC] Could not start UDP receiver on port " + port + ": " + ex.Message);
        }
    }

    private void StopListening()
    {
        running = false;

        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }

        if (receiveThread != null)
        {
            receiveThread.Join(100);
            receiveThread = null;
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (running)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                OscStringMessage message;
                if (!TryDecodeOscStringMessage(data, out message))
                {
                    continue;
                }

                lock (payloadLock)
                {
                    // overwrite with newest, we only care about the latest frame anyway
                    pendingAddress = message.address;
                    pendingJson = message.value;
                    pendingMessage = true;
                }
            }
            catch (SocketException)
            {
                // normal on shutdown when we close the socket, not a real error
                if (running)
                {
                    Debug.LogWarning("[IOCT OSC] Socket receive failed.");
                }
            }
            catch (ObjectDisposedException)
            {
                // also normal, happens on stop play / disable
            }
            catch (Exception ex)
            {
                if (running)
                {
                    Debug.LogWarning("[IOCT OSC] Receive error: " + ex.Message);
                }
            }
        }
    }

    private void ApplyStateJson(string json)
    {
        SyncStatePayload payload;
        try
        {
            payload = JsonUtility.FromJson<SyncStatePayload>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[IOCT OSC] Could not parse state JSON: " + ex.Message);
            return;
        }

        if (payload == null)
        {
            return;
        }

        currentCaptureName = payload.capture_name;
        latestFrameIndex = payload.frame_index;
        latestTimestampSeconds = payload.timestamp_seconds;
        latestWarningState = payload.tissue != null ? payload.tissue.warning_state : "unknown";

        if (payload.needle != null && payload.needle.tip_position != null)
        {
            latestNeedleTipAvailable = payload.needle.tip_position.available;
            latestNeedleTip = new Vector3(
                payload.needle.tip_position.x,
                payload.needle.tip_position.y,
                payload.needle.tip_position.z
            );

            if (payload.needle.injection_depth != null)
            {
                latestInjectionDepthAvailable = payload.needle.injection_depth.available;
                latestInjectionDepth = payload.needle.injection_depth.value;
            }
        }

        if (payload.tissue != null && payload.tissue.tension_normalized != null)
        {
            latestTensionAvailable = payload.tissue.tension_normalized.available;
            latestTensionNormalized = payload.tissue.tension_normalized.value;
        }

        hasState = true;
        hasNewState = true;

        if (verboseLogging)
        {
            Debug.Log("[IOCT OSC] Python frame " + latestFrameIndex + " at " + latestTimestampSeconds.ToString("F3") + "s");
        }
    }

    private static bool TryDecodeOscStringMessage(byte[] data, out OscStringMessage message)
    {
        message = new OscStringMessage();

        int offset = 0;
        string address;
        if (!TryReadOscString(data, ref offset, out address))
        {
            return false;
        }

        string typeTag;
        if (!TryReadOscString(data, ref offset, out typeTag))
        {
            return false;
        }

        if (typeTag != ",s")
        {
            return false;
        }

        string value;
        if (!TryReadOscString(data, ref offset, out value))
        {
            return false;
        }

        message.address = address;
        message.value = value;
        return true;
    }

    private static bool TryReadOscString(byte[] data, ref int offset, out string value)
    {
        value = "";

        if (offset < 0 || offset >= data.Length)
        {
            return false;
        }

        int start = offset;
        int end = start;
        while (end < data.Length && data[end] != 0)
        {
            end++;
        }

        if (end >= data.Length)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(data, start, end - start);

        // OSC strings are null-terminated and then padded up to a 4-byte boundary
        offset = end + 1;
        while (offset % 4 != 0)
        {
            offset++;
        }

        return true;
    }

    private struct OscStringMessage
    {
        public string address;
        public string value;
    }

    [Serializable]
    public class SyncStatePayload
    {
        public int protocol_version;
        public string capture_name;
        public int frame_index;
        public float timestamp_seconds;
        public string coordinate_space;
        public NeedlePayload needle;
        public TissuePayload tissue;
        public SemanticPayload semantic;
    }

    [Serializable]
    public class NeedlePayload
    {
        public VectorPayload tip_position;
        public VectorPayload direction;
        public ScalarPayload injection_depth;
        public ScalarPayload injection_angle;
    }

    [Serializable]
    public class TissuePayload
    {
        public ScalarPayload tension_normalized;
        public string warning_state;
        public string displacement_source;
    }

    [Serializable]
    public class SemanticPayload
    {
        public string labels_source;
    }

    [Serializable]
    public class VectorPayload
    {
        public bool available;
        public float x;
        public float y;
        public float z;
        public string space;
    }

    [Serializable]
    public class ScalarPayload
    {
        public bool available;
        public float value;
        public string unit;
    }
}
