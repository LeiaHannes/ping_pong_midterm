public struct PaddleMotion
{
    public PaddlePose From;
    public PaddlePose To;
    public float Duration;   // seconds to go from From to To

    public PaddleMotion(PaddlePose from, PaddlePose to, float duration)
    {
        From = from;
        To = to;
        Duration = duration;
    }

    // Paddle pose partway through the motion (t from 0 to 1)
    public PaddlePose At(float t) => PaddlePose.Lerp(From, To, t);
}