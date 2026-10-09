using TMPro;
using UnityEngine;

public class PracticeHUD : MonoBehaviour
{
    [SerializeField] private PracticeReferee referee;
    [SerializeField] private TMP_Text statsText;
    [SerializeField] private TMP_Text lastHitText;
    [SerializeField] private TMP_Text outcomeText;

    [Header("Outcome popup")]
    [SerializeField] private float outcomeShowTime = 1.2f;
    [SerializeField] private Color goodColor = new Color(0.4f, 1f, 0.4f);
    [SerializeField] private Color badColor = new Color(1f, 0.45f, 0.4f);

    private float outcomeTimer;

    void OnEnable()
    {
        referee.Judged += HandleJudged;
        referee.PlayerHit += HandleHit;
    }

    void OnDisable()
    {
        referee.Judged -= HandleJudged;
        referee.PlayerHit -= HandleHit;
    }

    void Start()
    {
        outcomeText.text = "";
        lastHitText.text = "Last hit: —";
        RefreshStats();
    }

    void Update()
    {
        // Fade the popup out over its last 0.3 seconds
        if (outcomeTimer > 0f)
        {
            outcomeTimer -= Time.unscaledDeltaTime;
            Color c = outcomeText.color;
            c.a = Mathf.Clamp01(outcomeTimer / 0.3f);
            outcomeText.color = c;
        }
    }

    void HandleJudged(RallyOutcome outcome)
    {
        outcomeText.text = Describe(outcome);
        outcomeText.color = outcome == RallyOutcome.GoodReturn ? goodColor : badColor;
        outcomeTimer = outcomeShowTime;
        RefreshStats();
    }

    void HandleHit(Contact c)
    {
        Vector3 v = c.Velocity;
        Vector3 dir = new Vector3(v.x, 0f, v.z).normalized;
        float topRps = Vector3.Dot(c.AngularVelocity, Vector3.Cross(Vector3.up, dir))
                       / PingPongSpec.RevsToRadians;
        string spin = topRps > 5f ? "topspin" : topRps < -5f ? "backspin" : "flat";

        lastHitText.text = $"Last hit: {v.magnitude * 3.6f:F0} km/h\n" +
                           $"{Mathf.Abs(topRps):F0} rev/s {spin}";
    }

    void RefreshStats()
    {
        float pct = referee.Attempts > 0 ? 100f * referee.GoodReturns / referee.Attempts : 0f;
        statsText.text = $"Returns: {referee.GoodReturns} / {referee.Attempts} ({pct:F0}%)\n" +
                         $"Streak: {referee.Streak}   Best: {referee.BestStreak}";
    }

    static string Describe(RallyOutcome o) => o switch
    {
        RallyOutcome.GoodReturn => "Good return!",
        RallyOutcome.IntoNet => "Into the net",
        RallyOutcome.Out => "Out",
        RallyOutcome.OwnSide => "Landed on your side",
        RallyOutcome.Missed => "Missed",
        RallyOutcome.Volley => "Volley: let it bounce first",
        RallyOutcome.MachineError => "Machine missed",
        _ => ""
    };
}