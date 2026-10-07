using System.Diagnostics.Contracts;
using UnityEngine;

public enum BallPhase {  InFlight, Resting, Dead }

public struct BallState
{
    public Vector3 Position;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;   // rad/s; axis = direction, speed = length
    public BallPhase Phase;

    public BallState(Vector3 position, Vector3 velocity, Vector3 angularVelocity = default)
    {
        Position = position;
        Velocity = velocity;
        AngularVelocity = angularVelocity;
        Phase = BallPhase.InFlight;
    }
}
