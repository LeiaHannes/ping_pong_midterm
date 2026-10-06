using System.Diagnostics.Contracts;
using UnityEngine;

public enum BallPhase {  InFlight, Resting, Dead }

public struct BallState
{
    public Vector3 Position;
    public Vector3 Velocity;
    public BallPhase Phase;

    public BallState(Vector3 position, Vector3 velocity)
    {
        Position = position;
        Velocity = velocity;
        Phase = BallPhase.InFlight;
    }
}
