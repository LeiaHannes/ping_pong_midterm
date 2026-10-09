using UnityEngine;

public class ContactLogger : MonoBehaviour
{
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
        string where = c.Position.z < 0f ? "your side" : "far side";
        float spinRps = c.AngularVelocity.magnitude / PingPongSpec.RevsToRadians;
        Debug.Log($"{c.Surface} ({where}) at {c.Position.z:F2} m — " +
                  $"speed {c.Velocity.magnitude:F1} m/s, spin {spinRps:F0} rev/s");
    }

    void HandleDied(BallSimulator ball)
    {
        Debug.Log("Ball died — rally over");
    }
}