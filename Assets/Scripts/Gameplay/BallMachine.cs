using UnityEngine;
using UnityEngine.InputSystem;

public class BallMachine : MonoBehaviour
{
    [Header("Ball")]
    [SerializeField] private BallSimulator ballPrefab;
    [SerializeField] private float ballLifetime = 5f;

    [Header("Launch")]
    [SerializeField] private float speed = 6f;          // m/s
    [SerializeField] private float elevation = 10f;     // degrees above horizontal
    [SerializeField] private float sideAngle = 0f;      // degrees, + = toward player's right

    [Header("Randomness (+/-)")]
    [SerializeField] private float speedJitter = 0.3f;
    [SerializeField] private float elevationJitter = 1f;
    [SerializeField] private float sideJitter = 3f;

    [Header("Auto fire (F = fire once, R = toggle auto)")]
    [SerializeField] private bool autoFire = false;
    [SerializeField] private float fireInterval = 1.5f;

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
            fireTimer += Time.deltaTime;
            if (fireTimer >= fireInterval)
            {
                fireTimer -= fireInterval;
                Fire();
            }
        }
    }

    public void Fire()
    {
        float s = speed + Random.Range(-speedJitter, speedJitter);
        float e = elevation + Random.Range(-elevationJitter, elevationJitter);
        float a = sideAngle + Random.Range(-sideJitter, sideJitter);

        Vector3 velocity = LaunchDirection(e, a) * s;

        BallSimulator ball = Instantiate(ballPrefab);
        ball.Launch(transform.position, velocity);
        Destroy(ball.gameObject, ballLifetime);
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