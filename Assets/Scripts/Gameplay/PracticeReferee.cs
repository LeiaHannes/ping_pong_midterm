using System;
using System.Collections.Generic;
using UnityEngine;

public enum RallyOutcome { GoodReturn, IntoNet, Out, OwnSide, Missed, Volley, MachineError }

public class PracticeReferee : MonoBehaviour
{
    public event Action<RallyOutcome> Judged;
    public event Action<Contact> PlayerHit;

    public int Attempts { get; private set; }
    public int GoodReturns { get; private set; }
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }

    enum Stage { Incoming, Hittable, Returned }

    class Tracker
    {
        public Stage Stage = Stage.Incoming;
        public bool TouchedNet;
        public bool Done;
    }

    private readonly Dictionary<BallSimulator, Tracker> balls = new Dictionary<BallSimulator, Tracker>();

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

    void HandleContact(BallSimulator ball, Contact c)
    {
        if (!balls.TryGetValue(ball, out Tracker t))
        {
            t = new Tracker();
            balls[ball] = t;
        }
        if (t.Done) return;

        bool yourSide = c.Position.z < 0f;

        switch (t.Stage)
        {
            case Stage.Incoming:
                if (c.Surface == ContactSurface.Table && yourSide) t.Stage = Stage.Hittable;
                else if (c.Surface == ContactSurface.Paddle) Finish(t, RallyOutcome.Volley);
                else if (c.Surface != ContactSurface.Net) Finish(t, RallyOutcome.MachineError);
                break;

            case Stage.Hittable:
                if (c.Surface == ContactSurface.Paddle)
                {
                    t.Stage = Stage.Returned;
                    PlayerHit?.Invoke(c);
                }
                else if (c.Surface != ContactSurface.Net)
                {
                    Finish(t, RallyOutcome.Missed);   // second bounce, floor, or spun back over
                }
                break;

            case Stage.Returned:
                switch (c.Surface)
                {
                    case ContactSurface.Net:
                        t.TouchedNet = true;
                        break;
                    case ContactSurface.Paddle:
                        PlayerHit?.Invoke(c);
                        break;
                    case ContactSurface.Table:
                        if (!yourSide) Finish(t, RallyOutcome.GoodReturn);
                        else Finish(t, t.TouchedNet ? RallyOutcome.IntoNet : RallyOutcome.OwnSide);
                        break;
                    case ContactSurface.Floor:
                        Finish(t, t.TouchedNet ? RallyOutcome.IntoNet : RallyOutcome.Out);
                        break;
                }
                break;
        }
    }

    void HandleDied(BallSimulator ball)
    {
        if (balls.TryGetValue(ball, out Tracker t) && !t.Done)
        {
            switch (t.Stage)
            {
                case Stage.Incoming: Finish(t, RallyOutcome.MachineError); break;
                case Stage.Hittable: Finish(t, RallyOutcome.Missed); break;
                case Stage.Returned: Finish(t, t.TouchedNet ? RallyOutcome.IntoNet : RallyOutcome.Out); break;
            }
        }
        balls.Remove(ball);
    }

    void Finish(Tracker t, RallyOutcome outcome)
    {
        t.Done = true;

        if (outcome != RallyOutcome.MachineError)
        {
            Attempts++;
            if (outcome == RallyOutcome.GoodReturn)
            {
                GoodReturns++;
                Streak++;
                BestStreak = Mathf.Max(BestStreak, Streak);
            }
            else
            {
                Streak = 0;
            }
        }

        Judged?.Invoke(outcome);
    }
}