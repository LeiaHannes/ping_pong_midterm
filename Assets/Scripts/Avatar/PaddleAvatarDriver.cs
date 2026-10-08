using UnityEngine;

public class PaddleAvatarDriver : MonoBehaviour, IAvatarSource
{
    [SerializeField] private Transform paddle;
    [SerializeField] private bool rightHanded = true;

    [Header("Body placement")]
    [SerializeField] private float bodyZ = -2.0f;
    [SerializeField] private float waistHeight = 0.8f;
    [SerializeField] private float headHeight = 0.75f;      // head pivot above the waist
    [SerializeField] private float shoulderOffset = 0.32f;  // sideways gap from body center to paddle
    [SerializeField] private float bodyXLimit = 0.9f;

    [Header("Swing response")]
    [SerializeField] private float paddleRestZ = -1.55f;
    [SerializeField] private float swingDistance = 0.35f;
    [SerializeField] private float swingLean = 15f;         // degrees forward at full swing
    [SerializeField] private float swingTwist = 20f;        // degrees of body turn at full swing

    [Header("Off hand (x is mirrored for handedness)")]
    [SerializeField] private Vector3 offHandOffset = new Vector3(0.3f, 0.3f, 0.15f);

    public AvatarPose GetPose()
    {
        float side = rightHanded ? 1f : -1f;
        Vector3 p = paddle.position;

        // How far into a swing is the paddle? 0 = at rest, 1 = fully forward
        float swing = Mathf.Clamp01((p.z - paddleRestZ) / swingDistance);

        // Torso: beside and behind the paddle, rising and falling a little with it
        float x = Mathf.Clamp(p.x - side * shoulderOffset, -bodyXLimit, bodyXLimit);
        float y = waistHeight + (p.y - 0.95f) * 0.3f;
        Vector3 torsoPos = new Vector3(x, y, bodyZ);
        Quaternion torsoRot = Quaternion.Euler(swing * swingLean, -side * swing * swingTwist, 0f);

        // Head: look at the nearest ball in flight, otherwise at the table
        Vector3 headPos = torsoPos + torsoRot * new Vector3(0f, headHeight, 0f);
        Vector3 lookTarget = new Vector3(0f, PingPongSpec.TableHeight, 0f);
        BallSimulator ball = ClosestBall(headPos);
        if (ball != null)
            lookTarget = ball.transform.position;
        Quaternion headRot = Quaternion.LookRotation(lookTarget - headPos);

        // Off hand: beside the body, pulled back during swings, with a gentle bob
        Vector3 handLocal = new Vector3(-side * offHandOffset.x, offHandOffset.y,
                                        offHandOffset.z - swing * 0.1f);
        Vector3 offHand = torsoPos + torsoRot * handLocal
                        + Vector3.up * Mathf.Sin(Time.time * 2f) * 0.01f;

        return new AvatarPose(torsoPos, torsoRot, headRot, offHand);
    }

    static BallSimulator ClosestBall(Vector3 from)
    {
        BallSimulator best = null;
        float bestDist = float.MaxValue;
        foreach (BallSimulator b in BallSimulator.Active)
        {
            if (b.State.Phase != BallPhase.InFlight) continue;
            float d = (b.transform.position - from).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = b;
            }
        }
        return best;
    }
}