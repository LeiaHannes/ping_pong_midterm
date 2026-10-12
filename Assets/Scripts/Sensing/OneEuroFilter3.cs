using UnityEngine;

// Speed-adaptive smoothing for a Vector3 (Casiez et al., "1€ Filter").
public class OneEuroFilter3
{
    public float MinCutoff = 1.5f;   // Hz; lower = steadier at rest, laggier
    public float Beta = 0.3f;        // higher = less lag during fast movement
    public float DCutoff = 1f;       // Hz; smoothing for the speed estimate itself

    private Vector3 value;
    private Vector3 velocity;
    private bool initialized;

    public Vector3 Velocity => velocity;

    public Vector3 Filter(Vector3 raw, float dt)
    {
        if (!initialized)
        {
            value = raw;
            velocity = Vector3.zero;
            initialized = true;
            return raw;
        }

        // Smooth the speed estimate, then use speed to choose how much to smooth the position
        Vector3 rawVelocity = (raw - value) / dt;
        velocity = Vector3.Lerp(velocity, rawVelocity, Alpha(DCutoff, dt));

        float cutoff = MinCutoff + Beta * velocity.magnitude;
        value = Vector3.Lerp(value, raw, Alpha(cutoff, dt));
        return value;
    }

    public void Reset() => initialized = false;

    static float Alpha(float cutoff, float dt)
    {
        float tau = 1f / (2f * Mathf.PI * cutoff);
        return 1f / (1f + tau / dt);
    }
}