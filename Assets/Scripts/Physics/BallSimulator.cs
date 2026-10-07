using UnityEngine;

public class BallSimulator : MonoBehaviour
{
    [Header("Testing")]
    [SerializeField] private bool launchOnStart = false;
    [SerializeField] private Vector3 testVelocity = new Vector3(0f, 2f, 4f);

    [Header("Lifecycle")]
    [SerializeField] private float deadLinger = 1f;   // seconds a dead ball stays visible

    [Header("Live readout")]
    [SerializeField] private float debugSpeed;
    [SerializeField] private float debugLastApex;     // height of last bounce peak above the table
    [SerializeField] private BallPhase debugPhase;
    [SerializeField] private float debugSpinRps;

    private BallState state;
    private bool launched;
    private bool dying;
    private float prevVelocityY;
    public BallState State => state;
    public static bool Interpolate = true;   // shared by all balls; toggled by debug keys
    private Vector3 prevPosition;

    void Start()
    {
        if (launchOnStart && !launched)
            Launch(transform.position, testVelocity);
    }

    public void Launch(Vector3 position, Vector3 velocity, Vector3 spin = default)
    {
        launched = true;
        state = new BallState(position, velocity, spin);
        transform.position = position;
        prevPosition = position;
    }

    void FixedUpdate()
    {
        prevPosition = state.Position;
        BallPhysics.Simulate(ref state, Time.fixedDeltaTime);

        // Readouts
        debugSpeed = state.Velocity.magnitude;
        debugPhase = state.Phase;
        debugSpinRps = state.AngularVelocity.magnitude / PingPongSpec.RevsToRadians;
        float vy = state.Velocity.y;
        if (prevVelocityY > 0f && vy <= 0f)   // was rising, now falling: that's the peak
            debugLastApex = state.Position.y - PingPongSpec.BallRadius - PingPongSpec.TableHeight;
        prevVelocityY = vy;

        // Cleanup
        if (state.Phase == BallPhase.Dead && !dying)
        {
            dying = true;
            Destroy(gameObject, deadLinger);
        }
    }
    void Update()
    {
        // Position
        if (Interpolate)
        {
            float alpha = (Time.time - Time.fixedTime) / Time.fixedDeltaTime;
            transform.position = Vector3.Lerp(prevPosition, state.Position, Mathf.Clamp01(alpha));
        }
        else
        {
            transform.position = state.Position;
        }

        // Visual spin
        Vector3 w = state.AngularVelocity;
        float degrees = w.magnitude * Mathf.Rad2Deg * Time.deltaTime;
        if (degrees > 0f)
            transform.rotation = Quaternion.AngleAxis(degrees, w.normalized) * transform.rotation;
    }
}