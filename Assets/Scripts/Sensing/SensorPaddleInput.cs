using UnityEngine;
using UnityEngine.InputSystem;

public class SensorPaddleInput : MonoBehaviour, IPaddleInput
{
    [SerializeField] private PoseReceiver receiver;
    [SerializeField] private bool rightHanded = true;
    [SerializeField] private ImuReceiver imu;

    [Header("Mapping")]
    [SerializeField] private Vector3 bodyOrigin = new Vector3(0f, 1.0f, -2.0f);
    [SerializeField] private Vector3 reachScale = new Vector3(1.2f, 1.2f, 1.5f);
    [SerializeField] private float lateralScale = 2.3f;    // meters per camera-frame width when stepping sideways
    [SerializeField] private float verticalScale = 1.3f;   // meters per camera-frame height when crouching
    [SerializeField] private float gripLength = 0.12f;     // wrist to blade center, along the forearm
    [SerializeField] private float offHandReach = 0.05f;   // wrist to the center of the off hand, along the forearm
    [SerializeField] private float minVisibility = 0.5f;
    [SerializeField] private float wristToBlade = 0.18f;   // wrist to blade center, along the handle (used with the IMU)

    [Header("Calibration (C = calibrate after a countdown)")]
    [SerializeField] private Vector3 readyPosition = new Vector3(0.3f, 0.95f, -1.55f);
    [SerializeField] private float countdownSeconds = 3f;
    [SerializeField] private Vector3 calibrationOffset;
    [SerializeField] private float tagSampleWindow = 0.5f;   // seconds before calibration to average the paddle tag
    [SerializeField] private int minTagSamples = 5;

    [Header("Smoothing and prediction")]
    [SerializeField] private float minCutoff = 1.2f;
    [SerializeField] private float beta = 0.6f;
    [SerializeField] private float maxExtrapolation = 0.05f;   // seconds
    [SerializeField] private float leadTime = 0f;              // seconds to predict ahead
    [SerializeField] private float restSpeed = 0.3f;           // m/s: below this, no prediction
    [SerializeField] private float fullSpeed = 1.0f;           // m/s: above this, full prediction
    [SerializeField] private float followTime = 0.025f;        // seconds; lower = snappier, higher = smoother

    [Header("Face angles until the IMU (scroll = tilt, Q/E = aim, middle click = reset)")]
    [SerializeField] private float tiltStep = 5f;
    [SerializeField] private float aimSpeed = 90f;
    [SerializeField] private float tilt;
    [SerializeField] private float aim;

    [Header("Live readout")]
    [SerializeField] private float debugSampleSpeed;

    private readonly OneEuroFilter3 filter = new OneEuroFilter3();
    private Vector2 rootAtCalibration;
    private double lastMessageTime = -1;
    private bool hasPose;
    private float calibrateAt = -1f;

    private Vector3 shownPos;       // where the paddle is
    private Vector3 followVel;      // SmoothDamp's internal velocity
    private Vector3 samplePos;      // newest filtered sample
    private Vector3 sampleVel;      // prediction velocity (zero when slowing or turning)
    private double sampleTime;
    private Vector3 prevFiltered;
    private Vector3 lastRawVel;

    // The off hand only needs smoothing, not prediction: nothing collides with it
    private readonly OneEuroFilter3 offHandFilter = new OneEuroFilter3();
    private double offHandLastTime = -1;
    private Vector3 offHandPos;
    private bool hasOffHand;

    private Vector3 tagNormalSum;
    private int tagSamples;
    private double lastTagSampleTime = -1;
    private Vector3 lastForearm = Vector3.up;

    int PaddleWrist => rightHanded ? 16 : 15;
    int PaddleElbow => rightHanded ? 14 : 13;
    int OffWrist => rightHanded ? 15 : 16;
    int OffElbow => rightHanded ? 13 : 14;

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;

        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0f) tilt -= tiltStep;
            if (scroll < 0f) tilt += tiltStep;
            if (mouse.middleButton.wasPressedThisFrame) { tilt = 0f; aim = 0f; }
        }
        if (kb != null)
        {
            if (kb.qKey.isPressed) aim -= aimSpeed * Time.deltaTime;
            if (kb.eKey.isPressed) aim += aimSpeed * Time.deltaTime;
            if (kb.cKey.wasPressedThisFrame)
            {
                calibrateAt = Time.unscaledTime + countdownSeconds;
                tagNormalSum = Vector3.zero;
                tagSamples = 0;
                Debug.Log($"Calibrating in {countdownSeconds:F0} s: hold your ready position");
            }
        }
        tilt = Mathf.Clamp(tilt, -60f, 60f);
        aim = Mathf.Clamp(aim, -45f, 45f);

        if (calibrateAt > 0f)
        {
            // Average the paddle tag's direction over the last moments of the countdown
            if (Time.unscaledTime >= calibrateAt - tagSampleWindow &&
                receiver.TryGetLatest(out PoseMessage tagMsg) &&
                tagMsg.t != lastTagSampleTime && tagMsg.pn != null && tagMsg.pn.Length == 3)
            {
                lastTagSampleTime = tagMsg.t;
                tagNormalSum += new Vector3(tagMsg.pn[0], tagMsg.pn[1], tagMsg.pn[2]);
                tagSamples++;
            }

            // Countdown finished
            if (Time.unscaledTime >= calibrateAt)
            {
                calibrateAt = -1f;
                Calibrate();
            }
        }

        // Keep the filter settings live-tunable from the Inspector
        filter.MinCutoff = minCutoff;
        filter.Beta = beta;
        offHandFilter.MinCutoff = minCutoff;
        offHandFilter.Beta = beta;
    }

    void Calibrate()
    {
        if (!receiver.TryGetLatest(out PoseMessage p))
        {
            Debug.LogWarning("Can't calibrate: no pose data");
            return;
        }
        rootAtCalibration = new Vector2(p.root[0], p.root[1]);
        calibrationOffset = Vector3.zero;
        calibrationOffset = readyPosition - MapHand(p, PaddleWrist, PaddleElbow, 0f)
                    - Vector3.up * wristToBlade;

        filter.Reset();
        hasPose = false;
        offHandFilter.Reset();
        hasOffHand = false;

        if (imu != null)
        {
            if (tagSamples >= minTagSamples)
            {
                imu.CalibrateHeadingFromTag(tagNormalSum.normalized);
                Debug.Log($"Heading set from the paddle tag ({tagSamples} samples)");
            }
            else
            {
                imu.CalibrateHeading();
                Debug.Log("Paddle tag not seen: assuming the paddle faces the table");
            }
        }

        Debug.Log("Paddle calibrated");
    }

    // Called by PaddleController every physics tick
    public PaddlePose GetPose()
    {
        double now = Time.timeAsDouble;
        Quaternion rotation = Quaternion.Euler(tilt, aim, 0f);

        Quaternion imuRotation = Quaternion.identity;
        bool imuActive = imu != null && imu.isActiveAndEnabled &&
                         imu.TryGetRotation(Time.fixedDeltaTime, out imuRotation);
        if (imuActive)
            rotation = imuRotation;

        // Take in a new camera sample if one has arrived
        if (receiver.TryGetLatest(out PoseMessage p) && p.t != lastMessageTime)
        {
            float dt = lastMessageTime < 0
                ? 1f / 30f
                : Mathf.Max(0.001f, (float)(p.t - lastMessageTime));
            lastMessageTime = p.t;

            if (p.vis[PaddleWrist] >= minVisibility)
            {
                Vector3 filtered = filter.Filter(MapHand(p, PaddleWrist, PaddleElbow, 0f), dt);
                lastForearm = (p.Landmark(PaddleWrist) - p.Landmark(PaddleElbow)).normalized;
                if (!hasPose)
                {
                    shownPos = filtered;
                    prevFiltered = filtered;
                    followVel = Vector3.zero;
                    lastRawVel = Vector3.zero;
                    hasPose = true;
                }

                Vector3 v = (filtered - prevFiltered) / dt;
                prevFiltered = filtered;
                float speed = v.magnitude;

                // Predict only while the hand is genuinely moving...
                float gate = Mathf.SmoothStep(0f, 1f, (speed - restSpeed) / (fullSpeed - restSpeed));

                // ...and back off immediately when it slows down or turns around
                float lastSpeed = lastRawVel.magnitude;
                if (lastSpeed > restSpeed)
                {
                    if (Vector3.Dot(v, lastRawVel) <= 0f) gate = 0f;
                    else if (speed < lastSpeed) gate *= speed / lastSpeed;
                }
                lastRawVel = v;

                samplePos = filtered;
                sampleVel = v * gate;
                // Measured from the previous tick so the paddle keeps moving on this one
                sampleTime = now - Time.fixedDeltaTime;
                debugSampleSpeed = speed;
            }
        }

        if (!hasPose)
            return new PaddlePose(readyPosition, rotation);

        // Where the hand should be now, with capped prediction
        float tau = (float)(now - sampleTime);
        Vector3 target = samplePos + sampleVel * (Mathf.Min(tau, maxExtrapolation) + leadTime);

        // Follow the target with a critically damped spring: smooth, and never overshoots
        shownPos = Vector3.SmoothDamp(shownPos, target, ref followVel, followTime,
                                      Mathf.Infinity, Time.fixedDeltaTime);
        // Blade center: up the handle from the wrist, along the IMU's orientation when available
        Vector3 bladeOffset = imuActive
            ? rotation * new Vector3(0f, wristToBlade, 0f)
            : lastForearm * gripLength;
        return new PaddlePose(shownPos + bladeOffset, rotation);
    }

    // World position of the tracked off hand. Returns false while it isn't visible.
    public bool TryGetOffHandPosition(out Vector3 position)
    {
        if (receiver.TryGetLatest(out PoseMessage p) && p.t != offHandLastTime)
        {
            float dt = offHandLastTime < 0
                ? 1f / 30f
                : Mathf.Max(0.001f, (float)(p.t - offHandLastTime));
            offHandLastTime = p.t;

            hasOffHand = p.vis[OffWrist] >= minVisibility;
            if (hasOffHand)
                offHandPos = offHandFilter.Filter(MapHand(p, OffWrist, OffElbow, offHandReach), dt);
        }

        position = offHandPos;
        return hasOffHand;
    }

    // Maps a tracked hand into table coordinates
    Vector3 MapHand(PoseMessage p, int wristIndex, int elbowIndex, float extend)
    {
        // The point of interest sits a little beyond the wrist, along the forearm
        Vector3 wrist = p.Landmark(wristIndex);
        Vector3 forearm = (wrist - p.Landmark(elbowIndex)).normalized;
        Vector3 hand = wrist + forearm * extend;

        // Stepping sideways or crouching, measured from the calibration stance
        Vector3 step = new Vector3(
            (p.root[0] - rootAtCalibration.x) * lateralScale,
            (p.root[1] - rootAtCalibration.y) * verticalScale,
            0f);

        return bodyOrigin + step + Vector3.Scale(hand, reachScale) + calibrationOffset;
    }
}