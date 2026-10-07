using UnityEngine;
using UnityEngine.Rendering;

public static class BallPhysics
{
    public const float SubstepDt = 0.001f;   // 1 ms

    public static void Simulate(ref BallState state, float totalDt)
    {
        int steps = Mathf.Max(1, Mathf.RoundToInt(totalDt / SubstepDt));
        float dt = totalDt / steps;
        for (int i = 0; i < steps; i++)
            Step(ref state, dt);
    }

    static void Step(ref BallState s, float dt)
    {
        switch (s.Phase)
        {
            case BallPhase.InFlight: StepFlight(ref s, dt); break;
            case BallPhase.Resting: StepRolling(ref s, dt); break;
            case BallPhase.Dead: return;
        }

        if (s.Position.magnitude > PingPongSpec.DeadZone)
            s.Phase = BallPhase.Dead;
    }

    static void StepFlight(ref BallState s, float dt)
    {
        Vector3 prev = s.Position;

        Vector3 v = s.Velocity;
        Vector3 accel = Vector3.down * PingPongSpec.Gravity
                      - PingPongSpec.DragK * v.magnitude * v;
        v += accel * dt;
        s.Position += v * dt;
        s.Velocity = v;

        if (CollideNet(ref s, prev))
            return;

        float r = PingPongSpec.BallRadius;
        float tableContactY = PingPongSpec.TableHeight + r;  // ball center height when touching table

        // Table: the ball crossed the surface from above during this substep, while over the table
        if (prev.y >= tableContactY && s.Position.y < tableContactY && IsOverTable(s.Position))
        {
            Bounce(ref s, tableContactY, PingPongSpec.TableRestitution);
            return;
        }

        // Floor: bouncing there to rest means the ball is dead
        if (s.Position.y < r)
        {
            Bounce(ref s, r, PingPongSpec.FloorRestitution);
            if (s.Phase == BallPhase.Resting)
                s.Phase = BallPhase.Dead;
        }
    }

    static void Bounce(ref BallState s, float contactY, float restitution)
    {
        Vector3 p = s.Position;
        Vector3 v = s.Velocity;

        p.y = contactY;                 // put the ball back on the surface
        v.y = -v.y * restitution;       // reverse and shrink vertical speed
        // Horizontal velocity is unchanged for now; step 3 adds friction and spin.

        if (v.y < PingPongSpec.RestSpeed)
        {
            v.y = 0f;
            s.Phase = BallPhase.Resting;
        }

        s.Position = p;
        s.Velocity = v;
    }

    // true if the ball hit the net during this substep
    static bool CollideNet(ref BallState s, Vector3 prev)
    {
        float r = PingPongSpec.BallRadius;
        float dz = s.Position.z - prev.z;

        // The face of the net the ball is approaching
        float contactZ = dz > 0f ? -r : r;

        bool crossed = dz > 0f
            ? prev.z <= contactZ && s.Position.z > contactZ
            : prev.z >= contactZ && s.Position.z < contactZ;
        if (!crossed) return false;

        // where it crossed
        float t = (contactZ - prev.z) / dz;
        Vector3 hit = Vector3.Lerp(prev, s.Position, t);

        bool withinNet = Mathf.Abs(hit.x) <= PingPongSpec.NetHalfWidth
                      && hit.y > PingPongSpec.TableHeight
                      && hit.y < PingPongSpec.TableHeight + PingPongSpec.NetHeight;
        if (!withinNet) return false;

        Vector3 v = s.Velocity;
        v.z = -v.z * PingPongSpec.NetRestitution;
        v.x *= PingPongSpec.NetDamping;
        v.y *= PingPongSpec.NetDamping;

        s.Position = hit;
        s.Velocity = v;
        return true;
    }

    static void StepRolling(ref BallState s, float dt)
    {
        Vector3 v = s.Velocity;
        float speed = v.magnitude;
        float slowdown = PingPongSpec.RollingFriction * PingPongSpec.Gravity * dt;

        if (speed < PingPongSpec.StopSpeed || slowdown >= speed)
        {
            s.Velocity = Vector3.zero;
            s.Phase = BallPhase.Dead;   // stopped on the table: rally over
            return;
        }

        Vector3 prev = s.Position;

        v -= v / speed * slowdown;      // shrink speed, keep direction
        s.Position += v * dt;
        s.Velocity = v;

        CollideNet(ref s, prev);

        if (!IsOverTable(s.Position))
            s.Phase = BallPhase.InFlight;   // rolled off the edge: start falling
    }

    static bool IsOverTable(Vector3 p) =>
        Mathf.Abs(p.x) <= PingPongSpec.TableWidth / 2f &&
        Mathf.Abs(p.z) <= PingPongSpec.TableLength / 2f;
}