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

    private BallState state;
    private bool launched;
    private bool dying;
    private float prevVelocityY;
    public BallState State => state;

    void Start()
    {
        if (launchOnStart && !launched)
            Launch(transform.position, testVelocity);
    }

    public void Launch(Vector3 position, Vector3 velocity)
    {
        launched = true;
        state = new BallState(position, velocity);
        transform.position = position;
    }

    void FixedUpdate()
    {
        BallPhysics.Simulate(ref state, Time.fixedDeltaTime);
        transform.position = state.Position;

        // Readouts
        debugSpeed = state.Velocity.magnitude;
        debugPhase = state.Phase;
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
}