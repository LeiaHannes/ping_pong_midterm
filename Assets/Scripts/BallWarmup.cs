using UnityEngine;
using UnityEngine.InputSystem;

public class BallWarmup : MonoBehaviour
{
    [SerializeField] private float gravity = 9.81f;
    [SerializeField] private float restitution_table = 0.99f;
    [SerializeField] private float restitution_floor = 0.5f;
    [SerializeField] private float tableHeight = 0.76f;
    [SerializeField] private float tableWidth = 1.525f;
    [SerializeField] private float tableLength = 2.74f;
    [SerializeField] private float ballRadius = 0.02f;

    private Vector3 velocity;
    private Vector3 startPosition;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        startPosition = transform.position;
    }
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        velocity.y -= gravity * dt;
        transform.position += velocity * dt;
        Vector3 p = transform.position;
        // check to see if the ball is on the table
        if ((Mathf.Abs(p.x) + ballRadius > tableWidth / 2f || Mathf.Abs(p.z) + ballRadius > tableLength / 2f) && (p.y - ballRadius < tableHeight))
        {
            if (p.y - ballRadius < 0 && velocity.y < 0f)
            {
                p.y = ballRadius;
                transform.position = p;
                velocity.y = -velocity.y * restitution_floor;
                Debug.Log($"Bounce! Upward speed: {velocity.y:F2} m/s");
            }
        }
        else if (p.y - ballRadius < tableHeight && velocity.y < 0f)
        {
            p.y = tableHeight + ballRadius;
            transform.position = p;
            velocity.y = -velocity.y * restitution_table;
            Debug.Log($"Bounce! Upward speed: {velocity.y:F2} m/s");
        }
    }

    void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            transform.position = startPosition;
            velocity = new Vector3(0f, 0f, 0.1f);
        }
    }

    /* Draw table edges
     * void OnDrawGizmos()
    {
        // Yellow outline: where your code thinks the table surface is
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(new Vector3(0f, tableHeight, 0f),
                            new Vector3(tableWidth, 0f, tableLength));

        // Red lines: the x = ±tableWidth/2 edges, extended past the table
        Gizmos.color = Color.red;
        float edge = tableWidth / 2f;
        Gizmos.DrawLine(new Vector3(edge, tableHeight, -3f), new Vector3(edge, tableHeight, 3f));
        Gizmos.DrawLine(new Vector3(-edge, tableHeight, -3f), new Vector3(-edge, tableHeight, 3f));

        // Small white sphere at the world origin
        Gizmos.color = Color.white;
        Gizmos.DrawSphere(Vector3.zero, 0.05f);
    }*/
}
