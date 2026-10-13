using UnityEngine;

public class OpponentPaddleMotion : MonoBehaviour
{
    [Header("Rest (world space)")]
    [SerializeField] private Vector3 restPosition = new Vector3(-0.3f, 0.95f, 1.55f);

    [Header("Swing")]
    [SerializeField] private float backswing = 0.3f;        // meters behind the hit point the swing comes from
    [SerializeField] private float followThrough = 0.25f;   // meters past the hit point it carries on
    [SerializeField] private float swingLead = 0.08f;       // seconds before contact the forward swing starts
    [SerializeField] private float moveTime = 0.12f;        // smoothing while getting into position
    [SerializeField] private float turnSharpness = 12f;

    enum Phase { Resting, Preparing, Swinging }

    private Phase phase = Phase.Resting;
    private Vector3 hitPoint;
    private Vector3 aim = Vector3.back;
    private float hitTime;
    private Vector3 swingStart;
    private Vector3 moveVelocity;

    // Called by the opponent when it knows where and when it will hit the ball
    public void PrepareFor(Vector3 point, float secondsUntilHit, Vector3 aimDirection)
    {
        hitPoint = point;
        aim = aimDirection;
        hitTime = Time.time + secondsUntilHit;
        phase = Phase.Preparing;
    }

    void Update()
    {
        float now = Time.time;

        if (phase == Phase.Preparing && now >= hitTime - swingLead)
        {
            phase = Phase.Swinging;
            swingStart = transform.position;
        }
        if (phase == Phase.Swinging && now >= hitTime + swingLead)
        {
            phase = Phase.Resting;
            moveVelocity = aim * (followThrough / swingLead);   // carry the swing's momentum into the recovery
        }

        switch (phase)
        {
            case Phase.Preparing:
                // Get behind the ball, face toward where it will be sent
                MoveToward(hitPoint - aim * backswing, Quaternion.LookRotation(aim));
                break;

            case Phase.Swinging:
                // From wherever the backswing ended, through the ball exactly at contact time, into the follow-through
                float t = Mathf.InverseLerp(hitTime - swingLead, hitTime + swingLead, now);
                transform.position = t < 0.5f
                    ? Vector3.Lerp(swingStart, hitPoint, t * 2f)
                    : Vector3.Lerp(hitPoint, hitPoint + aim * followThrough, (t - 0.5f) * 2f);
                transform.rotation = Quaternion.LookRotation(aim);
                break;

            default:
                MoveToward(restPosition, Quaternion.LookRotation(Vector3.back));
                break;
        }
    }

    void MoveToward(Vector3 position, Quaternion rotation)
    {
        transform.position = Vector3.SmoothDamp(transform.position, position, ref moveVelocity, moveTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, rotation,
                                              1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
    }
}