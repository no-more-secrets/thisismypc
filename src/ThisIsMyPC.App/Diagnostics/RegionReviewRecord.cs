#if DEBUG
namespace ThisIsMyPC.App.Diagnostics;

internal sealed record RegionReviewRecord
{
    public int SchemaVersion { get; init; } = 4;
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
    public required int? SelectedFigureNumber { get; init; }
    public required IReadOnlyList<RegionReviewFigure> Figures { get; init; }
    public required IReadOnlyList<RegionReviewCapture> Captures { get; init; }
    public int NextFigureNumber { get; init; } = 1;
    private IReadOnlyList<RegionReviewFigure> resolvedFigures = [];
    // Older records and external writers can encode an empty history as null.
    public IReadOnlyList<RegionReviewFigure> ResolvedFigures
    {
        get => resolvedFigures;
        init => resolvedFigures = value ?? [];
    }
    public required bool Suspended { get; init; }
}

internal sealed record RegionReviewFigure
{
    public required int Number { get; init; }
    public int? ImageFigureNumber { get; init; }
    public required string Id { get; init; }
    public required RegionReviewBounds Bounds { get; init; }
    public string? Note { get; init; }
    public DateTime? ResolvedAtUtc { get; init; }
    public string? ResolutionNote { get; init; }
    public required string PageRoute { get; init; }
    public required string CaptureId { get; init; }
    public required DateTime CapturedAtUtc { get; init; }
    public required string ImagePath { get; init; }
}

internal sealed record RegionReviewCapture
{
    public required string Id { get; init; }
    public required string PageRoute { get; init; }
    public required DateTime CapturedAtUtc { get; init; }
    public required string ImagePath { get; init; }
    public required double RenderScale { get; init; }
    public required int PixelWidth { get; init; }
    public required int PixelHeight { get; init; }
    public string? RawImagePath { get; init; }
    public required double LogicalWidth { get; init; }
    public required double LogicalHeight { get; init; }
    public required string LayoutState { get; init; }
}

internal sealed record RegionReviewBounds
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
}
internal sealed record RegionReviewCommand
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required string FigureId { get; init; }
    public required string Status { get; init; }
    public string? ResolutionNote { get; init; }
}
internal sealed record RegionReviewReceipt
{
    public required bool Applied { get; init; }
    public string? Error { get; init; }
}
#endif
