using UnityEngine;
using UnityEngine.InputSystem;

public class BallMachine : MonoBehaviour
{
    [Header("Ball")]
    [SerializeField] private BallSimulator ballPrefab;

    [Header("Launch")]
    [SerializeField] private float speed = 6f;          // m/s
    [SerializeField] private float elevation = 15f;     // degrees above horizontal
    [SerializeField] private float sideAngle = 0f;      // degrees, + = toward player's right

    [Header("Randomness (+/-)")]
    [SerializeField] private float speedJitter = 0.3f;
    [SerializeField] private float elevationJitter = 1f;
    [SerializeField] private float sideJitter = 3f;

    [Header("Spin (revolutions per second)")]
    [SerializeField] private float topspin = 0f;     // + topspin, - backspin
    [SerializeField] private float sidespin = 0f;    // + curves toward the player's right
    [SerializeField] private float spinJitter = 2f;

    [Header("Auto fire (F = fire once, R = toggle auto)")]
    [SerializeField] private bool autoFire = false;
    [SerializeField] private float fireInterval = 1.5f;
    [SerializeField] private bool waitForRallyEnd = true;   // hold auto-fire while a ball is still in play

    public Vector3 LaunchPosition => transform.position;

    public Vector3 NominalVelocity() => LaunchDirection(elevation, sideAngle) * speed;

    public Vector3 NominalSpin() => SpinVector(LaunchDirection(elevation, sideAngle), topspin, sidespin);

    // Converts "topspin/sidespin in rev/s" into an angular velocity vector for a ball moving in 'direction'
    static Vector3 SpinVector(Vector3 direction, float topRps, float sideRps)
    {
        Vector3 horizontal = new Vector3(direction.x, 0f, direction.z).normalized;
        Vector3 topspinAxis = Vector3.Cross(Vector3.up, horizontal);
        return (topspinAxis * topRps - Vector3.up * sideRps) * PingPongSpec.RevsToRadians;
    }

    private float fireTimer;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.fKey.wasPressedThisFrame) Fire();
            if (kb.rKey.wasPressedThisFrame) autoFire = !autoFire;
        }

        if (autoFire)
        {
            if (waitForRallyEnd && AnyBallInPlay())
            {
                fireTimer = 0f;   // count the interval from when the rally ends
            }
            else
            {
                fireTimer += Time.deltaTime;
                if (fireTimer >= fireInterval)
                {
                    fireTimer -= fireInterval;
                    Fire();
                }
            }
        }
    }

    // True while any ball is still flying or rolling (dead balls are about to be removed)
    static bool AnyBallInPlay()
    {
        foreach (BallSimulator b in BallSimulator.Active)
            if (b.State.Phase != BallPhase.Dead)
                return true;
        return false;
    }

    public void Fire()
    {
        float s = speed + Random.Range(-speedJitter, speedJitter);
        float e = elevation + Random.Range(-elevationJitter, elevationJitter);
        float a = sideAngle + Random.Range(-sideJitter, sideJitter);
        float top = topspin + Random.Range(-spinJitter, spinJitter);
        float side = sidespin + Random.Range(-spinJitter, spinJitter);

        Vector3 direction = LaunchDirection(e, a);
        Vector3 velocity = direction * s;
        Vector3 spin = SpinVector(direction, top, side);

        BallSimulator ball = Instantiate(ballPrefab);
        ball.Launch(transform.position, velocity, spin);
    }

    // The machine fires toward -z, i.e. toward the player.
    static Vector3 LaunchDirection(float elevationDeg, float sideDeg)
    {
        return Quaternion.Euler(-elevationDeg, 180f - sideDeg, 0f) * Vector3.forward;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.03f);
        Gizmos.DrawRay(transform.position, LaunchDirection(elevation, sideAngle) * 0.5f);
    }
}