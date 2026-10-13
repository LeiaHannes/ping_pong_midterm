using System.Collections.Generic;
using UnityEngine;

public class ScriptedOpponent : MonoBehaviour
{
    [Header("Where and how it returns")]
    [SerializeField] private float hitZ = 1.6f;                                // returns the ball once it passes this point behind the far end
    [SerializeField] private Vector3 target = new Vector3(0f, 0f, -0.685f);    // landing spot: the middle of the player's half
    [SerializeField] private float returnSpeed = 5f;                           // horizontal speed of the return, m/s
    [SerializeField] private float topspinRps = 0f;
    [SerializeField] private OpponentPaddleMotion paddleMotion;

    private readonly HashSet<BallSimulator> hitByPlayer = new HashSet<BallSimulator>();
    private readonly HashSet<BallSimulator> readyToReturn = new HashSet<BallSimulator>();
    private readonly List<BallSimulator> returningNow = new List<BallSimulator>();
    private readonly List<Contact> simContacts = new List<Contact>();

    void OnEnable()
    {
        BallSimulator.AnyContact += HandleContact;
        BallSimulator.AnyDied += HandleDied;
    }

    void OnDisable()
    {
        BallSimulator.AnyContact -= HandleContact;
        BallSimulator.AnyDied -= HandleDied;
    }

    // Only balls the player hit, which then landed on the far side, get returned
    void HandleContact(BallSimulator ball, Contact c)
    {
        switch (c.Surface)
        {
            case ContactSurface.Paddle:
                hitByPlayer.Add(ball);
                readyToReturn.Remove(ball);
                break;

            case ContactSurface.OpponentPaddle:
                hitByPlayer.Remove(ball);
                readyToReturn.Remove(ball);
                break;

            case ContactSurface.Table:
                if (c.Position.z > 0f && hitByPlayer.Remove(ball))
                {
                    readyToReturn.Add(ball);
                    if (paddleMotion != null &&
                        PredictHit(ball.State, out Vector3 hitPoint, out float timeToHit))
                    {
                        paddleMotion.PrepareFor(hitPoint, timeToHit, AimFrom(hitPoint));
                    }
                }
                break;
        }
    }

    // Where and when the ball will reach the hitting point, by simulating ahead
    bool PredictHit(BallState from, out Vector3 point, out float time)
    {
        BallState sim = from;
        for (int step = 1; step <= 200; step++)    // up to 2 seconds
        {
            BallPhysics.Simulate(ref sim, 0.01f);
            if (sim.Phase != BallPhase.InFlight) break;
            if (sim.Position.z >= hitZ && sim.Velocity.z > 0f)
            {
                point = sim.Position;
                time = step * 0.01f;
                return true;
            }
        }
        point = Vector3.zero;
        time = 0f;
        return false;
    }

    // Horizontal direction from a point toward the target
    Vector3 AimFrom(Vector3 start) =>
        new Vector3(target.x - start.x, 0f, target.z - start.z).normalized;

    void HandleDied(BallSimulator ball)
    {
        hitByPlayer.Remove(ball);
        readyToReturn.Remove(ball);
    }

    void FixedUpdate()
    {
        // Collect first: a set can't be changed while looping over it
        returningNow.Clear();
        foreach (BallSimulator ball in readyToReturn)
        {
            BallState s = ball.State;
            if (s.Phase == BallPhase.InFlight && s.Position.z >= hitZ && s.Velocity.z > 0f)
                returningNow.Add(ball);
        }

        foreach (BallSimulator ball in returningNow)
        {
            readyToReturn.Remove(ball);
            Return(ball);
        }
    }

    void Return(BallSimulator ball)
    {
        Vector3 start = ball.State.Position;
        Vector3 direction = AimFrom(start);
        Vector3 spin = Vector3.Cross(Vector3.up, direction) * topspinRps * PingPongSpec.RevsToRadians;

        // Bisection: find the upward speed that lands the ball on the target
        float low = -3f;    // lands short
        float high = 8f;    // lands long
        for (int i = 0; i < 25; i++)
        {
            float mid = (low + high) / 2f;
            Vector3 velocity = direction * returnSpeed + Vector3.up * mid;
            if (LandsShort(start, velocity, spin)) low = mid;
            else high = mid;
        }

        // 'high' is always on the safe side: it never lands short, so it always clears the net
        ball.Redirect(direction * returnSpeed + Vector3.up * high, spin);
    }

    // Simulates a shot and reports whether it lands before the target.
    // Hitting the net, or landing back on the opponent's side, counts as short.
    bool LandsShort(Vector3 start, Vector3 velocity, Vector3 spin)
    {
        var sim = new BallState(start, velocity, spin);
        for (int step = 0; step < 300; step++)    // up to 3 seconds
        {
            simContacts.Clear();
            BallPhysics.Simulate(ref sim, 0.01f, null, simContacts);

            if (simContacts.Count > 0)
            {
                Contact first = simContacts[0];
                if (first.Surface == ContactSurface.Table) return first.Position.z > target.z;
                if (first.Surface == ContactSurface.Net) return true;
                return first.Position.z > 0f;     // floor: short if it fell before the player's half
            }
            if (sim.Phase == BallPhase.Dead) return false;
        }
        return false;
    }
}