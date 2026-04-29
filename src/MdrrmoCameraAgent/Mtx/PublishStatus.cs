namespace MdrrmoCameraAgent.Mtx;

public enum PublishStatus
{
    Unknown,            // no baseline yet OR /v3/paths/list unreachable
    Publishing,         // bytesReceived increasing since last sample
    DegradedNoFrames,   // path exists but bytes flat, OR path missing
    DegradedAuth,       // 401/403 from upstream — not reliably detectable
                        // from /v3/paths/list, included for forward-compat
    Disabled,           // operator-disabled (set externally)
}

public static class PublishStatusExtensions
{
    public static string ToWireString(this PublishStatus s) => s switch
    {
        PublishStatus.Publishing       => "publishing",
        PublishStatus.DegradedNoFrames => "degraded_no_frames",
        PublishStatus.DegradedAuth     => "degraded_auth",
        PublishStatus.Disabled         => "disabled",
        PublishStatus.Unknown          => "unknown",
        _                              => throw new ArgumentOutOfRangeException(nameof(s), s, "No wire string defined for PublishStatus value"),
    };
}
