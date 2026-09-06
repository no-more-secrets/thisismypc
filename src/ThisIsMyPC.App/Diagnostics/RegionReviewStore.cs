#if DEBUG
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThisIsMyPC.App.Diagnostics;

internal class RegionReviewStore : IDisposable
{
    private readonly string outputDirectory;
    private FileStream? lease;
    private readonly bool useLegacyMigration;
    private string sessionId = Guid.NewGuid().ToString("N");
    private readonly Process currentProcess = Process.GetCurrentProcess();

    internal RegionReviewStore(string? outputDirectory = null)
    {
        this.outputDirectory = Path.GetFullPath(outputDirectory ?? FindDefaultOutputDirectory());
        useLegacyMigration = outputDirectory is null;
    }

    internal string SessionId => sessionId;
    internal string OutputDirectory => outputDirectory;
    internal void Acquire()
    {
        Directory.CreateDirectory(outputDirectory);
        lease ??= new FileStream(Path.Combine(outputDirectory, "session.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
    }

    internal string ValidateImagePath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(outputDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Review image is outside the review directory.");
        return full;
    }

    internal RegionReviewRecord? Read()
    {
        var latest = Path.Combine(outputDirectory, "latest.json");
        if (!File.Exists(latest) && useLegacyMigration)
            ImportLegacy();
        if (!File.Exists(latest)) return null;
        var record = JsonSerializer.Deserialize(File.ReadAllText(latest), RegionReviewJsonContext.Default.RegionReviewRecord)
            ?? throw new InvalidDataException("Review record is empty.");
        if (record.SchemaVersion is not (3 or 4)) throw new InvalidDataException("Unsupported review schema.");
        var all = record.Figures.Concat(record.ResolvedFigures).ToArray();
        if (all.Any(f => f.Number < 1) || all.Select(f => f.Id).Distinct().Count() != all.Length
            || record.Figures.Select(f => f.Number).Distinct().Count() != record.Figures.Count)
            throw new InvalidDataException("Review figure identities are invalid.");
        foreach (var capture in record.Captures)
        {
            if (!double.IsFinite(capture.LogicalWidth) || !double.IsFinite(capture.LogicalHeight)
                || capture.LogicalWidth <= 0 || capture.LogicalHeight <= 0
                || !double.IsFinite(capture.RenderScale) || capture.RenderScale <= 0
                || capture.PixelWidth <= 0 || capture.PixelHeight <= 0
                || (long)capture.PixelWidth * capture.PixelHeight > 100_000_000)
                throw new InvalidDataException("Review capture dimensions are invalid.");
            ValidateImagePath(capture.ImagePath);
            if (!string.IsNullOrEmpty(capture.RawImagePath)) ValidateImagePath(capture.RawImagePath);
        }
        foreach (var figure in all)
        {
            ValidateImagePath(figure.ImagePath);
            if (!record.Captures.Any(c => c.Id == figure.CaptureId))
                throw new InvalidDataException("Review figure capture is missing from the record.");
        }
        sessionId = record.SessionId;
        return record;
    }

    private void ImportLegacy()
    {
        var root = Path.GetDirectoryName(outputDirectory)!;
        var legacy = Path.Combine(root, "artifacts", "diagnostics", "region-review");
        var latest = Path.Combine(legacy, "latest.json");
        if (!File.Exists(latest)) return;
        var record = JsonSerializer.Deserialize(File.ReadAllText(latest), RegionReviewJsonContext.Default.RegionReviewRecord);
        if (record is null || record.SchemaVersion != 3) return;
        string CopyImage(string oldPath)
        {
            if (string.IsNullOrEmpty(oldPath)) return oldPath;
            var source = Path.GetFullPath(oldPath);
            if (!source.StartsWith(legacy + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Legacy image is outside its capture directory.");
            var target = Path.Combine(outputDirectory, Path.GetFileName(source));
            if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target);
            return target;
        }
        Write(record with { SchemaVersion = 4, ImagePath = CopyImage(record.ImagePath),
            Figures = record.Figures.Select(f => f with { ImagePath = CopyImage(f.ImagePath) }).ToArray(),
            Captures = record.Captures.Select(c => c with { ImagePath = CopyImage(c.ImagePath) }).ToArray(),
            NextFigureNumber = record.Figures.Select(f => f.Number).DefaultIfEmpty(0).Max() + 1 });
    }

    public void Dispose()
    {
        lease?.Dispose();
        lease = null;
        currentProcess.Dispose();
        GC.SuppressFinalize(this);
    }

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
        return Path.Combine(root, ".region-review");
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
[JsonSerializable(typeof(RegionReviewCommand))]
[JsonSerializable(typeof(RegionReviewReceipt))]
internal sealed partial class RegionReviewJsonContext : JsonSerializerContext;
#endif
