using UnityEngine;

public enum ContactSurface { Table, Net, Paddle, Floor, OpponentPaddle }

public struct Contact
{
    public ContactSurface Surface;
    public Vector3 Position;           // ball center at the moment of contact
    public Vector3 Velocity;           // ball velocity just after
    public Vector3 AngularVelocity;    // ball spin just after

    public Contact(ContactSurface surface, BallState s)
    {
        Surface = surface;
        Position = s.Position;
        Velocity = s.Velocity;
        AngularVelocity = s.AngularVelocity;
    }
}