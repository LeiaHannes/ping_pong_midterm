using UnityEngine;

public static class BallPhysics
{
    public const float SubstepDt = 0.001f;   // 1 ms

    // Advance the ball by totalDt seconds using small substeps.
    public static void Simulate(ref BallState state, float totalDt)
    {
        int steps = Mathf.Max(1, Mathf.RoundToInt(totalDt / SubstepDt));
        float dt = totalDt / steps;
        for (int i = 0; i < steps; i++)
            Step(ref state, dt);
    }

    static void Step(ref BallState state, float dt)
    {
        if (state.Phase != BallPhase.InFlight) return;

        Vector3 v = state.Velocity;
        Vector3 accel = Vector3.down * PingPongSpec.Gravity
                      - PingPongSpec.DragK * v.magnitude * v;

        v += accel * dt;               // update velocity first...
        state.Position += v * dt;      // ...then position with the NEW velocity
        state.Velocity = v;
    }
}