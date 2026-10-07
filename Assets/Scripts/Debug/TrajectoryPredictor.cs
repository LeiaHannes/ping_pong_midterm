using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BallMachine))]
public class TrajectoryPredictor : MonoBehaviour
{
    [SerializeField] private float duration = 1.5f;     // seconds to look ahead
    [SerializeField] private float step = 0.01f;        // seconds between line points
    [SerializeField] private Color lineColor = Color.green;
    [SerializeField] private Color bounceColor = Color.yellow;
    [SerializeField] private float markerRadius = 0.03f;

    private readonly List<Vector3> points = new List<Vector3>();
    private readonly List<Vector3> bounces = new List<Vector3>();

    void OnDrawGizmos()
    {
        var machine = GetComponent<BallMachine>();
        if (machine == null) return;

        Predict(machine.LaunchPosition, machine.NominalVelocity(), machine.NominalSpin());

        Gizmos.color = lineColor;
        for (int i = 1; i < points.Count; i++)
            Gizmos.DrawLine(points[i - 1], points[i]);

        Gizmos.color = bounceColor;
        foreach (Vector3 b in bounces)
            Gizmos.DrawWireSphere(b, markerRadius);
    }

    void Predict(Vector3 position, Vector3 velocity, Vector3 spin)
    {
        points.Clear();
        bounces.Clear();

        var sim = new BallState(position, velocity, spin);
        points.Add(sim.Position);

        int count = Mathf.CeilToInt(duration / step);
        for (int i = 0; i < count; i++)
        {
            float vyBefore = sim.Velocity.y;
            BallPhysics.Simulate(ref sim, step);
            points.Add(sim.Position);

            // Was falling, now isn't: it hit the table or floor
            if (vyBefore < 0f && sim.Velocity.y >= 0f)
                bounces.Add(sim.Position);

            if (sim.Phase == BallPhase.Dead)
                break;
        }
    }
}