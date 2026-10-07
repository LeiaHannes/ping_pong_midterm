using UnityEngine;
using UnityEngine.Rendering;

public static class BallPhysics
{
    public const float SubstepDt = 0.001f;   // 1 ms

    public static void Simulate(ref BallState state, float totalDt, PaddleMotion? paddle = null)
    {
        int steps = Mathf.Max(1, Mathf.RoundToInt(totalDt / SubstepDt));
        float dt = totalDt / steps;
        for (int i = 0; i < steps; i++)
        {
            float t0 = (float)i / steps;         // where this substep starts within the tick (0..1)
            float t1 = (float)(i + 1) / steps;   // where it ends
            Step(ref state, dt, paddle, t0, t1);
        }
    }

    static void Step(ref BallState s, float dt, PaddleMotion? paddle, float t0, float t1)
    {
        switch (s.Phase)
        {
            case BallPhase.InFlight: StepFlight(ref s, dt, paddle, t0, t1); break;
            case BallPhase.Resting: StepRolling(ref s, dt); break;
            case BallPhase.Dead: return;
        }

        if (s.Position.magnitude > PingPongSpec.DeadZone)
            s.Phase = BallPhase.Dead;
    }

    static void StepFlight(ref BallState s, float dt, PaddleMotion? paddle, float t0, float t1)
    {
        Vector3 prev = s.Position;

        Vector3 v = s.Velocity;
        Vector3 w = s.AngularVelocity;
        Vector3 accel = Vector3.down * PingPongSpec.Gravity
                      - PingPongSpec.DragK * v.magnitude * v
                      + PingPongSpec.MagnusK * Vector3.Cross(w, v);
        v += accel * dt;
        s.Position += v * dt;
        s.Velocity = v;
        s.AngularVelocity = w * (1f - PingPongSpec.SpinDecay * dt);

        if (paddle.HasValue && CollidePaddle(ref s, prev, paddle.Value, t0, t1))
            return;

        if (CollideNet(ref s, prev))
            return;

        float r = PingPongSpec.BallRadius;
        float tableContactY = PingPongSpec.TableHeight + r;  // ball center height when touching table

        // Table: the ball crossed the surface from above during this substep, while over the table
        if (prev.y >= tableContactY && s.Position.y < tableContactY && IsOverTable(s.Position))
        {
            Bounce(ref s, tableContactY, PingPongSpec.TableRestitution, PingPongSpec.TableFriction);
            return;
        }

        // Floor: bouncing there to rest means the ball is dead
        if (s.Position.y < r)
        {
            Bounce(ref s, r, PingPongSpec.FloorRestitution, PingPongSpec.FloorFriction);
            if (s.Phase == BallPhase.Resting)
                s.Phase = BallPhase.Dead;
        }
    }

    static void Bounce(ref BallState s, float contactY, float restitution, float friction)
    {
        Vector3 p = s.Position;
        p.y = contactY;
        s.Position = p;

        ApplyContact(ref s, Vector3.up, Vector3.zero, restitution, friction);

        if (s.Velocity.y < PingPongSpec.RestSpeed)
        {
            Vector3 v = s.Velocity;
            v.y = 0f;
            s.Velocity = v;
            s.Phase = BallPhase.Resting;
        }
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
        s.AngularVelocity *= PingPongSpec.NetDamping;
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
        s.AngularVelocity = Vector3.Cross(Vector3.up, v) / PingPongSpec.BallRadius;

        CollideNet(ref s, prev);

        if (!IsOverTable(s.Position))
            s.Phase = BallPhase.InFlight;   // rolled off the edge: start falling
    }

    static bool IsOverTable(Vector3 p) =>
        Mathf.Abs(p.x) <= PingPongSpec.TableWidth / 2f &&
        Mathf.Abs(p.z) <= PingPongSpec.TableLength / 2f;

    // Ball hits a surface. 'normal' points from the surface toward the ball; the surface moves at surfaceVelocity.
    static void ApplyContact(ref BallState s, Vector3 normal, Vector3 surfaceVelocity, float restitution, float friction)
    {
        float r = PingPongSpec.BallRadius;
        Vector3 u = s.Velocity - surfaceVelocity;      // velocity relative to the surface
        float un = Vector3.Dot(u, normal);             // negative = approaching
        if (un >= 0f) return;                          // already moving apart

        // Normal direction: reverse and shrink
        float normalImpulse = -(1f + restitution) * un;
        Vector3 normalChange = normalImpulse * normal;

        // Along the surface: how fast is the contact point sliding?
        Vector3 contactOffset = -normal * r;
        Vector3 slip = u + Vector3.Cross(s.AngularVelocity, contactOffset);
        slip -= Vector3.Dot(slip, normal) * normal;    // keep only the part along the surface

        Vector3 rollImpulse = -slip * (2f / 5f);
        float maxFriction = friction * normalImpulse;
        Vector3 frictionChange = rollImpulse.magnitude <= maxFriction
            ? rollImpulse
            : rollImpulse.normalized * maxFriction;

        s.Velocity += normalChange + frictionChange;
        s.AngularVelocity += Vector3.Cross(contactOffset, frictionChange) / (2f / 3f * r * r);
    }

    static bool CollidePaddle(ref BallState s, Vector3 prevBall, PaddleMotion m, float t0, float t1)
    {
        float r = PingPongSpec.BallRadius;
        PaddlePose a = m.At(t0);   // paddle at the start of this substep
        PaddlePose b = m.At(t1);   // paddle at the end

        // Signed distance of the ball center from the face plane, before and after
        float d0 = Vector3.Dot(prevBall - a.Position, a.Normal);
        float d1 = Vector3.Dot(s.Position - b.Position, b.Normal);

        float side = d0 >= 0f ? 1f : -1f;    // +1 = forehand face, -1 = backhand face
        float gap0 = side * d0 - r;          // >= 0 means not touching
        float gap1 = side * d1 - r;
        if (gap0 < 0f || gap1 >= 0f) return false;   // didn't go from apart to touching

        // Exactly when during the substep did they touch?
        float f = gap0 / (gap0 - gap1);
        PaddlePose c = m.At(Mathf.Lerp(t0, t1, f));
        Vector3 ballAtContact = Vector3.Lerp(prevBall, s.Position, f);

        // Is the contact point on the blade, or did the ball pass beside it?
        Vector3 fromCenter = ballAtContact - c.Position;
        Vector3 inPlane = fromCenter - Vector3.Dot(fromCenter, c.Normal) * c.Normal;
        if (inPlane.magnitude > PingPongSpec.PaddleRadius) return false;

        Vector3 normal = side * c.Normal;

        // Velocity of that point on the blade over the tick (includes paddle rotation)
        Vector3 local = Quaternion.Inverse(c.Rotation) * inPlane;
        Vector3 pointFrom = m.From.Position + m.From.Rotation * local;
        Vector3 pointTo = m.To.Position + m.To.Rotation * local;
        Vector3 surfaceVelocity = (pointTo - pointFrom) / m.Duration;

        // Place the ball touching the face, then bounce it
        s.Position = c.Position + inPlane + normal * r;
        ApplyContact(ref s, normal, surfaceVelocity, PingPongSpec.PaddleRestitution, PingPongSpec.PaddleFriction);
        return true;
    }
}