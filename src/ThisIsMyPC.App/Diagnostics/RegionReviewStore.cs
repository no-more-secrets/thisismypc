#if DEBUG
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThisIsMyPC.App.Diagnostics;

internal class RegionReviewStore
{
    private readonly string outputDirectory;
    private string sessionId = Guid.NewGuid().ToString("N");
    private readonly Process currentProcess = Process.GetCurrentProcess();

    internal RegionReviewStore(string? outputDirectory = null)
    {
        this.outputDirectory = outputDirectory ?? FindDefaultOutputDirectory();
    }

    internal string OutputDirectory => outputDirectory;
    internal void StartSession() => sessionId = Guid.NewGuid().ToString("N");

    internal RegionReviewRecord CreateRecord(
        bool active,
        string selectionId,
        DateTime capturedAtUtc,
        string windowTitle,
        RegionReviewBounds bounds,
        double renderScale,
        int pixelWidth,
        int pixelHeight,
        string imagePath,
        int? selectedFigureNumber,
        IReadOnlyList<RegionReviewFigure> figures,
        IReadOnlyList<RegionReviewCapture> captures,
        bool suspended = false) => new()
        {
            SessionId = sessionId,
            SelectionId = selectionId,
            Active = active,
            CapturedAtUtc = capturedAtUtc,
            WindowTitle = windowTitle,
            Bounds = bounds,
            RenderScale = renderScale,
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            ImagePath = imagePath,
            ProcessId = currentProcess.Id,
            ProcessStartedAtUtc = currentProcess.StartTime.ToUniversalTime(),
            BuildIdentity = GetBuildIdentity(),
            SelectedFigureNumber = selectedFigureNumber,
            Figures = figures,
            Captures = captures,
            Suspended = suspended,
        };

    internal virtual void Write(RegionReviewRecord record)
    {
        Directory.CreateDirectory(outputDirectory);
        var latestPath = Path.Combine(outputDirectory, "latest.json");
        var temporaryPath = Path.Combine(outputDirectory, $"latest-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(record, RegionReviewJsonContext.Default.RegionReviewRecord));
        File.Move(temporaryPath, latestPath, true);
    }

    private static string FindDefaultOutputDirectory()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "ThisIsMyPC.slnx")))
            directory = Path.GetDirectoryName(directory);

        var root = directory ?? AppContext.BaseDirectory;
        return Path.Combine(root, "artifacts", "diagnostics", "region-review");
    }

    private static string GetBuildIdentity()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
            return "unknown";

        return $"{Path.GetFileName(executablePath)}:{File.GetLastWriteTimeUtc(executablePath):O}";
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(RegionReviewRecord))]
internal sealed partial class RegionReviewJsonContext : JsonSerializerContext;
#endif
