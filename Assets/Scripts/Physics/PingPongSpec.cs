using UnityEngine;

public static class PingPongSpec
{
    // Ball
    public const float BallRadius = 0.02f;       // m
    public const float BallMass = 0.0027f;       // kg

    // Environment
    public const float Gravity = 9.81f;          // m/s^2
    public const float AirDensity = 1.2f;        // kg/m^3
    public const float DragCoefficient = 0.45f;

    // Table: centered at world origin, long axis along z
    public const float TableHeight = 0.76f;
    public const float TableWidth = 1.525f;      // along x
    public const float TableLength = 2.74f;      // along z
    public const float NetHeight = 0.1525f;
    public const float NetOverhang = 0.1525f;

    // Derived: drag deceleration = DragK * |v| * v
    public const float BallArea = Mathf.PI * BallRadius * BallRadius;
    public const float DragK = 0.5f * AirDensity * DragCoefficient * BallArea / BallMass;

    // Collisions
    public const float TableRestitution = 0.90f;  // calibrated: 30 cm drop -> ~23 cm bounce, with drag
    public const float FloorRestitution = 0.5f;
    public const float RestSpeed = 0.15f;         // bounce slower than this = stop bouncing
    public const float RollingFriction = 0.03f;   // rolling resistance coefficient
    public const float StopSpeed = 0.02f;         // rolling slower than this = stopped
    public const float DeadZone = 6f;             // farther than this from the origin = dead
}