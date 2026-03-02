namespace LLMeta.App.Models;

public readonly record struct VideoStreamStats(
    bool IsConnected,
    uint LastSequence,
    ulong LastTimestampUnixMs,
    uint DroppedFrames,
    int LastPayloadSize,
    long LastLatencyMs,
    uint QueueDepth,
    ulong RawRtpPackets,
    double ReceivedFps,
    double ReceivedBitrateKbps,
    uint PliRequests
);
