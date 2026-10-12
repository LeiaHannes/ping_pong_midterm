using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

[Serializable]
public class ImuMessage
{
    public double t;      // capture time, seconds since 1970 (UTC)
    public float[] ypr;   // yaw, pitch, roll in degrees
}

public class ImuReceiver : MonoBehaviour
{
    [SerializeField] private int port = 5066;

    [Header("Axis mapping (flip a sign if that rotation is mirrored)")]
    [SerializeField] private bool invertYaw;
    [SerializeField] private bool invertPitch;
    [SerializeField] private bool invertRoll;
    [SerializeField] private Vector3 mountingEuler = new Vector3(90f, 0f, 0f);   // paddle rotation when the sensor reads zero

    [Header("Smoothing")]
    [SerializeField] private float followSharpness = 40f;

    [Header("Live readout")]
    [SerializeField] private Vector3 debugYawPitchRoll;
    [SerializeField] private float debugRateHz;

    private UdpClient client;
    private Thread thread;
    private volatile bool running;

    private readonly object gate = new object();
    private ImuMessage latest;
    private int receivedCount;
    private float rateTimer;

    private Quaternion headingOffset = Quaternion.identity;
    private Quaternion smoothed = Quaternion.identity;
    private bool hasSmoothed;

    // Smoothed paddle rotation, including heading calibration. Call once per physics tick.
    public bool TryGetRotation(float dt, out Quaternion rotation)
    {
        rotation = smoothed;
        if (!TryGetRaw(out Quaternion raw)) return false;

        Quaternion target = headingOffset * raw;
        if (!hasSmoothed)
        {
            smoothed = target;
            hasSmoothed = true;
        }
        else
        {
            smoothed = Quaternion.Slerp(smoothed, target, 1f - Mathf.Exp(-followSharpness * dt));
        }

        rotation = smoothed;
        return true;
    }

    // Turn the world so the paddle face currently points toward the table
    public void CalibrateHeading()
    {
        if (!TryGetRaw(out Quaternion raw)) return;

        Vector3 normal = raw * Vector3.forward;
        Vector3 flat = new Vector3(normal.x, 0f, normal.z);
        if (flat.sqrMagnitude < 0.01f)
        {
            Debug.LogWarning("Can't calibrate heading with the paddle face pointing straight up or down");
            return;
        }
        headingOffset = Quaternion.FromToRotation(flat.normalized, Vector3.forward);
        hasSmoothed = false;
    }

    bool TryGetRaw(out Quaternion rotation)
    {
        ImuMessage m;
        lock (gate) { m = latest; }
        rotation = Quaternion.identity;
        if (m == null) return false;

        float yaw = m.ypr[0] * (invertYaw ? -1f : 1f);
        float pitch = m.ypr[1] * (invertPitch ? -1f : 1f);
        float roll = m.ypr[2] * (invertRoll ? -1f : 1f);

        // Unity applies roll (z), then pitch (x), then yaw (y): the same order the sensor uses
        Quaternion device = Quaternion.Euler(pitch, yaw, roll);
        rotation = device * Quaternion.Euler(mountingEuler);
        return true;
    }

    void OnEnable()
    {
        client = new UdpClient(port);
        running = true;
        thread = new Thread(ReceiveLoop) { IsBackground = true };
        thread.Start();
    }

    void OnDisable()
    {
        running = false;
        client?.Close();
        thread?.Join(200);
        client = null;
        thread = null;
    }

    void ReceiveLoop()
    {
        var from = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = client.Receive(ref from);
                var msg = JsonUtility.FromJson<ImuMessage>(Encoding.UTF8.GetString(data));
                if (msg?.ypr == null || msg.ypr.Length != 3) continue;

                lock (gate)
                {
                    latest = msg;
                    receivedCount++;
                }
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { if (!running) break; }
            catch (Exception e) { Debug.LogWarning($"IMU receive error: {e.Message}"); }
        }
    }

    void Update()
    {
        ImuMessage m;
        lock (gate) { m = latest; }
        if (m != null)
            debugYawPitchRoll = new Vector3(m.ypr[0], m.ypr[1], m.ypr[2]);

        rateTimer += Time.unscaledDeltaTime;
        if (rateTimer >= 1f)
        {
            int n;
            lock (gate) { n = receivedCount; receivedCount = 0; }
            debugRateHz = n / rateTimer;
            rateTimer = 0f;
        }
    }
}