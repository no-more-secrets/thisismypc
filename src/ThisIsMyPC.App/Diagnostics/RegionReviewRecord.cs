#if DEBUG
namespace ThisIsMyPC.App.Diagnostics;

internal sealed record RegionReviewRecord
{
    public int SchemaVersion { get; init; } = 1;
    public required string SessionId { get; init; }
    public required string SelectionId { get; init; }
    public required bool Active { get; init; }
    public required DateTime CapturedAtUtc { get; init; }
    public required string WindowTitle { get; init; }
    public required RegionReviewBounds Bounds { get; init; }
    public required double RenderScale { get; init; }
    public required int PixelWidth { get; init; }
    public required int PixelHeight { get; init; }
    public required string ImagePath { get; init; }
    public required int ProcessId { get; init; }
    public required DateTime ProcessStartedAtUtc { get; init; }
    public required string BuildIdentity { get; init; }
}

internal sealed record RegionReviewBounds
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
}
#endif
