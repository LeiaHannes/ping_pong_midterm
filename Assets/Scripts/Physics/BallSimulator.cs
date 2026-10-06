using UnityEngine;

public class BallSimulator : MonoBehaviour
{
    [Header("Testing")]
    [SerializeField] private bool launchOnStart = true;
    [SerializeField] private Vector3 testVelocity = new Vector3(0f, 2f, 4f);

    [Header("Live readout")]
    [SerializeField] private float debugSpeed;

    private BallState state;
    public BallState State => state;

    void Start()
    {
        if (launchOnStart)
            Launch(transform.position, testVelocity);
    }

    public void Launch(Vector3 position, Vector3 velocity)
    {
        state = new BallState(position, velocity);
        transform.position = position;
    }

    void FixedUpdate()
    {
        BallPhysics.Simulate(ref state, Time.fixedDeltaTime);
        transform.position = state.Position;
        debugSpeed = state.Velocity.magnitude;
    }
}