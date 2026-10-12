using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

[Serializable]
public class PoseMessage
{
    public double t;      // capture time, seconds since 1970 (UTC)
    public float[] lm;    // 33 landmarks x (x, y, z): Unity axes, meters, centered on the hips
    public float[] vis;   // 33 visibility values, 0..1
    public float[] root;  // hip center in the camera image: [right, up], roughly -0.5..0.5

    public Vector3 Landmark(int i) => new Vector3(lm[3 * i], lm[3 * i + 1], lm[3 * i + 2]);
}

public class PoseReceiver : MonoBehaviour
{
    [SerializeField] private int port = 5065;

    [Header("Live readout")]
    [SerializeField] private float debugAgeMs;
    [SerializeField] private float debugRateHz;

    private UdpClient client;
    private Thread thread;
    private volatile bool running;

    private readonly object gate = new object();
    private PoseMessage latest;
    private int receivedCount;
    private float rateTimer;

    // Called from the main thread by anything that wants the newest pose
    public bool TryGetLatest(out PoseMessage pose)
    {
        lock (gate) { pose = latest; }
        return pose != null;
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
        client?.Close();      // unblocks the waiting Receive call
        thread?.Join(200);
        client = null;
        thread = null;
    }

    // Runs on a background thread: wait for packets, parse, store the newest
    void ReceiveLoop()
    {
        var from = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = client.Receive(ref from);
                var msg = JsonUtility.FromJson<PoseMessage>(Encoding.UTF8.GetString(data));
                if (msg?.lm == null || msg.lm.Length != 99) continue;

                lock (gate)
                {
                    latest = msg;
                    receivedCount++;
                }
            }
            catch (ObjectDisposedException) { break; }   // socket closed while stopping
            catch (SocketException) { if (!running) break; }
            catch (Exception e) { Debug.LogWarning($"Pose receive error: {e.Message}"); }
        }
    }

    void Update()
    {
        if (TryGetLatest(out PoseMessage p))
        {
            double nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            debugAgeMs = (float)((nowSeconds - p.t) * 1000.0);
        }

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