using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

[Serializable]
public class ImuMessage
{
    public double t;      // sender's clock, seconds since 1970 (UTC)
    public float[] ypr;   // LEGO motor: yaw, pitch, roll in degrees
    public float[] q;     // UNO Q: quaternion (w, x, y, z), sensor -> world, right-handed with z up
}

public class ImuReceiver : MonoBehaviour
{
    [SerializeField] private int port = 5066;

    [Header("LEGO motor axis mapping (yaw/pitch/roll messages)")]
    [SerializeField] private bool invertYaw;
    [SerializeField] private bool invertPitch;
    [SerializeField] private bool invertRoll;
    [SerializeField] private Vector3 mountingEuler = new Vector3(90f, 0f, 0f);   // paddle rotation when the sensor reads zero

    [Header("Quaternion sensor mounting (B = two-pose mounting calibration)")]
    [SerializeField] private float countdownSeconds = 3f;
    [SerializeField] private Vector3 debugMountEuler;

    [Header("Smoothing")]
    [SerializeField] private float followSharpness = 80f;

    [Header("Live readout")]
    [SerializeField] private Vector3 debugPaddleEuler;
    [SerializeField] private float debugRateHz;

    private const string MountKey = "imu_mount_";

    private UdpClient client;
    private Thread thread;
    private volatile bool running;

    private readonly object gate = new object();
    private ImuMessage latest;
    private int receivedCount;
    private float rateTimer;

    private Quaternion mount = Quaternion.identity;      // paddle frame -> sensor frame
    private bool hasMount;
    private bool warnedNoMount;
    private bool calibrating;

    private Quaternion headingOffset = Quaternion.identity;
    private Quaternion smoothed = Quaternion.identity;
    private bool hasSmoothed;

    void Awake()
    {
        LoadMount();
    }

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

    // Like CalibrateHeading, but matches the face direction the camera measured from the paddle tag
    public void CalibrateHeadingFromTag(Vector3 tagNormal)
    {
        if (!TryGetRaw(out Quaternion raw)) return;

        Vector3 imuNormal = raw * Vector3.forward;
        Vector3 imuFlat = new Vector3(imuNormal.x, 0f, imuNormal.z);
        Vector3 tagFlat = new Vector3(tagNormal.x, 0f, tagNormal.z);
        if (imuFlat.sqrMagnitude < 0.01f || tagFlat.sqrMagnitude < 0.01f)
        {
            Debug.LogWarning("Can't calibrate heading with the paddle face pointing straight up or down");
            return;
        }
        headingOffset = Quaternion.FromToRotation(imuFlat.normalized, tagFlat.normalized);
        hasSmoothed = false;
    }

    // Paddle rotation before heading correction, from whichever kind of message is arriving
    bool TryGetRaw(out Quaternion rotation)
    {
        if (TryGetSensorRotation(out Quaternion sensor))
        {
            if (!hasMount && !warnedNoMount)
            {
                Debug.LogWarning("IMU mounting not calibrated yet: press B and follow the prompts");
                warnedNoMount = true;
            }
            rotation = sensor * mount;
            return true;
        }

        ImuMessage m;
        lock (gate) { m = latest; }
        rotation = Quaternion.identity;
        if (m?.ypr == null || m.ypr.Length != 3) return false;

        float yaw = m.ypr[0] * (invertYaw ? -1f : 1f);
        float pitch = m.ypr[1] * (invertPitch ? -1f : 1f);
        float roll = m.ypr[2] * (invertRoll ? -1f : 1f);

        // Unity applies roll (z), then pitch (x), then yaw (y): the same order the LEGO sensor uses
        Quaternion device = Quaternion.Euler(pitch, yaw, roll);
        rotation = device * Quaternion.Euler(mountingEuler);
        return true;
    }

    // The sensor's own orientation in Unity's world axes (quaternion messages only)
    bool TryGetSensorRotation(out Quaternion sensor)
    {
        ImuMessage m;
        lock (gate) { m = latest; }
        sensor = Quaternion.identity;
        if (m?.q == null || m.q.Length != 4) return false;

        // Right-handed, z-up  ->  Unity's left-handed, y-up:
        // swap the y and z axes and negate the vector part
        sensor = new Quaternion(-m.q[1], -m.q[3], -m.q[2], m.q[0]);
        return true;
    }

    // Learns how the sensor sits on the paddle from gravity in two known poses
    IEnumerator MountCalibration()
    {
        calibrating = true;

        Debug.Log($"Mounting 1/2: hold your READY position (face toward the table, handle down) in {countdownSeconds:F0} s");
        yield return new WaitForSecondsRealtime(countdownSeconds);
        if (!TryGetSensorRotation(out Quaternion poseA))
        {
            Debug.LogWarning("Mounting calibration needs quaternion data from the sensor");
            calibrating = false;
            yield break;
        }
        // In the ready pose, gravity points down the handle, so the paddle's tip (+y) is opposite gravity
        Vector3 tipInSensor = -(Quaternion.Inverse(poseA) * Vector3.down);
        Debug.Log("Captured ready pose.");

        float wait = countdownSeconds + 2f;
        Debug.Log($"Mounting 2/2: lay the paddle FACE UP on a flat surface in {wait:F0} s");
        yield return new WaitForSecondsRealtime(wait);
        if (!TryGetSensorRotation(out Quaternion poseB))
        {
            calibrating = false;
            yield break;
        }
        // Lying face up, the paddle's face normal (+z) is opposite gravity
        Vector3 normalInSensor = -(Quaternion.Inverse(poseB) * Vector3.down);

        // The face normal is the more precise measurement (the paddle lies flat), so it leads
        mount = Quaternion.LookRotation(normalInSensor, tipInSensor);
        hasMount = true;
        SaveMount();

        headingOffset = Quaternion.identity;
        hasSmoothed = false;
        calibrating = false;
        Debug.Log("Mounting saved. Now press C in your ready position to set the heading.");
    }

    void SaveMount()
    {
        PlayerPrefs.SetFloat(MountKey + "x", mount.x);
        PlayerPrefs.SetFloat(MountKey + "y", mount.y);
        PlayerPrefs.SetFloat(MountKey + "z", mount.z);
        PlayerPrefs.SetFloat(MountKey + "w", mount.w);
        PlayerPrefs.Save();
        debugMountEuler = mount.eulerAngles;
    }

    void LoadMount()
    {
        if (!PlayerPrefs.HasKey(MountKey + "w")) return;

        mount = new Quaternion(
            PlayerPrefs.GetFloat(MountKey + "x"),
            PlayerPrefs.GetFloat(MountKey + "y"),
            PlayerPrefs.GetFloat(MountKey + "z"),
            PlayerPrefs.GetFloat(MountKey + "w"));
        hasMount = true;
        debugMountEuler = mount.eulerAngles;
    }

    void OnEnable()
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
                return;   // browsers can't open UDP sockets or threads: sensor input is unavailable on the web
        #endif
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
                bool valid = (msg?.q != null && msg.q.Length == 4) ||
                             (msg?.ypr != null && msg.ypr.Length == 3);
                if (!valid) continue;

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
        var kb = Keyboard.current;
        if (kb != null && kb.bKey.wasPressedThisFrame && !calibrating)
            StartCoroutine(MountCalibration());

        if (TryGetRaw(out Quaternion raw))
            debugPaddleEuler = (headingOffset * raw).eulerAngles;

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