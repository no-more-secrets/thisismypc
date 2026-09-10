using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ThisIsMyPC.Modules.Hardware.Cooling;

/// <summary>A lossless editor for the supported parts of FanControl 226 profiles.</summary>
public sealed class FanControlProfileDocument
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    private readonly byte[] _original;
    private readonly JsonObject _root;

    private FanControlProfileDocument(byte[] original, JsonObject root)
    {
        _original = original;
        _root = root;
        var main = Object(root["Main"], "Main");
        Curves = Array(main["FanCurves"], "FanCurves")
            .Select(node => new FanControlProfileCurve(Object(node, "curve"))).ToList();
        if (Curves.Select(curve => curve.Name).Distinct(StringComparer.Ordinal).Count() != Curves.Count)
            throw new FormatException("The profile has duplicate curve names. Rename them in FanControl first.");
        Controls = Array(main["Controls"], "Controls")
            .Select(node => new FanControlProfileControl(Object(node, "fan"))).ToList();
        if (Controls.Select(control => control.Identifier).Distinct(StringComparer.Ordinal).Count() != Controls.Count)
            throw new FormatException("The profile has duplicate fan identifiers.");
        foreach (var control in Controls)
            if (control.CurveName is { } name && Curves.All(curve => curve.Name != name))
                throw new FormatException($"The fan '{control.Name}' refers to a missing curve '{name}'.");
    }

    public IReadOnlyList<FanControlProfileControl> Controls { get; }
    public IReadOnlyList<FanControlProfileCurve> Curves { get; }

    /// <summary>Parses a bounded profile. Unsupported versions and ambiguous objects are refused.</summary>
    public static FanControlProfileDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaximumBytes)
            throw new FormatException("The profile is too large to edit.");
        ReadOnlyMemory<byte> json = bytes;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            json = json[3..];
        using var parsed = JsonDocument.Parse(json);
        CheckProperties(parsed.RootElement);
        var root = Object(JsonNode.Parse(json.Span), "profile");
        if (root["__VERSION__"]?.ToString() != "226")
            throw new FormatException("Profile editing supports FanControl version 226. Open this profile in FanControl instead.");
        return new FanControlProfileDocument(bytes.ToArray(), root);
    }

    /// <summary>Returns original bytes when unchanged. Only supported fields change otherwise.</summary>
    public byte[] Serialize()
    {
        var root = (JsonObject)_root.DeepClone();
        var main = Object(root["Main"], "Main");
        var curves = Array(main["FanCurves"], "FanCurves");
        for (var i = 0; i < Curves.Count; i++)
            Curves[i].Apply(Object(curves[i], "curve"));
        var controls = Array(main["Controls"], "Controls");
        for (var i = 0; i < Controls.Count; i++)
        {
            var target = Object(controls[i], "fan");
            Controls[i].Apply(target);
            var name = Controls[i].CurveName;
            if (name is null)
            {
                if (target["SelectedFanCurve"] is not null)
                    target["SelectedFanCurve"] = null;
                continue;
            }
            var index = Curves.ToList().FindIndex(curve => curve.Name == name);
            if (index < 0)
                throw new FormatException($"The fan '{Controls[i].Name}' refers to a missing curve.");
            if (target["SelectedFanCurve"] is JsonObject selected && selected["Name"]?.GetValue<string>() == name)
                Curves[index].Apply(selected);
            else
                target["SelectedFanCurve"] = curves[index]!.DeepClone();
        }
        return JsonNode.DeepEquals(root, _root) ? _original.ToArray()
            : Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CheckProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new FormatException($"Duplicate JSON property: {property.Name}.");
                CheckProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckProperties(item);
    }

    internal static JsonObject Object(JsonNode? node, string name) => node as JsonObject
        ?? throw new FormatException($"The profile has an invalid {name} object.");
    internal static JsonArray Array(JsonNode? node, string name) => node as JsonArray
        ?? throw new FormatException($"The profile has an invalid {name} list.");
    internal static string RequiredText(JsonObject node, string key) => node[key]?.GetValue<string>() is { Length: > 0 } text
        ? text : throw new FormatException($"The profile is missing {key}.");
    internal static decimal Number(JsonObject node, string key) => node[key]?.GetValue<decimal>()
        ?? throw new FormatException($"The profile is missing {key}.");
    internal static void PercentInRange(decimal value)
    {
        if (value is < 0 or > 100) throw new FormatException("Fan speeds must be between 0 and 100 percent.");
    }
}

public enum FanControlCurveKind { Unsupported, Flat, Graph }

/// <summary>One editable graph point. Temperature uses FanControl's stored Celsius scale.</summary>
public sealed record FanControlCurvePoint(decimal Temperature, decimal Percent);

public sealed class FanControlProfileCurve
{
    private readonly decimal _originalPercent;
    private readonly List<FanControlCurvePoint> _originalPoints = [];
    private readonly decimal _minimumTemperature;
    private readonly decimal _maximumTemperature;

    internal FanControlProfileCurve(JsonObject node)
    {
        Name = FanControlProfileDocument.RequiredText(node, "Name");
        if (node["CommandMode"]?.GetValue<int>() != 0) return;
        if (node["Points"] is JsonArray points)
        {
            Kind = FanControlCurveKind.Graph;
            _minimumTemperature = FanControlProfileDocument.Number(node, "MinimumTemperature");
            _maximumTemperature = FanControlProfileDocument.Number(node, "MaximumTemperature");
            foreach (var point in points)
            {
                var pair = point?.GetValue<string>().Split(',');
                if (pair is not { Length: 2 } || !decimal.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature)
                    || !decimal.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                    throw new FormatException($"The curve '{Name}' has an invalid graph point.");
                Points.Add(new(temperature, percent));
            }
            _originalPoints = Points.ToList();
            TemperatureSource = node["SelectedTempSource"]?["NickName"]?.GetValue<string>()
                ?? node["SelectedTempSource"]?["Name"]?.GetValue<string>() ?? "Existing temperature sensor";
        }
        else if (node["Percent"] is not null)
        {
            Kind = FanControlCurveKind.Flat;
            Percent = _originalPercent = FanControlProfileDocument.Number(node, "Percent");
        }
    }

    public string Name { get; }
    public FanControlCurveKind Kind { get; }
    public string? TemperatureSource { get; }
    public decimal Percent { get; set; }
    public List<FanControlCurvePoint> Points { get; } = [];

    internal void Apply(JsonObject target)
    {
        if (Kind == FanControlCurveKind.Flat && Percent != _originalPercent)
        {
            FanControlProfileDocument.PercentInRange(Percent);
            target["Percent"] = Percent;
        }
        if (Kind != FanControlCurveKind.Graph || Points.SequenceEqual(_originalPoints)) return;
        if (Points.Count is < 2 or > 100)
            throw new FormatException($"The curve '{Name}' needs between 2 and 100 points.");
        decimal? previous = null;
        foreach (var point in Points)
        {
            FanControlProfileDocument.PercentInRange(point.Percent);
            if (point.Temperature < _minimumTemperature || point.Temperature > _maximumTemperature)
                throw new FormatException($"Temperatures in '{Name}' must stay between {_minimumTemperature} and {_maximumTemperature} Celsius.");
            if (previous is { } value && point.Temperature <= value)
                throw new FormatException($"Temperatures in '{Name}' must increase from left to right.");
            previous = point.Temperature;
        }
        target["Points"] = new JsonArray(Points.Select(point => (JsonNode?)JsonValue.Create(
            string.Create(CultureInfo.InvariantCulture, $"{point.Temperature},{point.Percent}"))).ToArray());
    }
}

public sealed class FanControlProfileControl
{
    private readonly string? _originalNickname;
    private readonly bool _originalEnabled;
    private readonly bool _originalManualControl;
    private readonly decimal _originalManualPercent;

    internal FanControlProfileControl(JsonObject node)
    {
        Identifier = FanControlProfileDocument.RequiredText(node, "Identifier");
        Name = FanControlProfileDocument.RequiredText(node, "Name");
        NickName = _originalNickname = node["NickName"]?.GetValue<string>();
        Enabled = _originalEnabled = node["Enable"]?.GetValue<bool>() ?? throw new FormatException("A fan is missing Enable.");
        ManualControl = _originalManualControl = node["ManualControl"]?.GetValue<bool>() ?? throw new FormatException("A fan is missing ManualControl.");
        ManualPercent = _originalManualPercent = FanControlProfileDocument.Number(node, "ManualControlValue");
        CurveName = node["SelectedFanCurve"]?["Name"]?.GetValue<string>();
    }

    public string Identifier { get; }
    public string Name { get; }
    public string? NickName { get; set; }
    public bool Enabled { get; set; }
    public bool ManualControl { get; set; }
    public decimal ManualPercent { get; set; }
    public string? CurveName { get; set; }

    internal void Apply(JsonObject target)
    {
        if (NickName?.Length > 120) throw new FormatException("Fan names must be at most 120 characters.");
        if (NickName != _originalNickname) target["NickName"] = NickName;
        if (Enabled != _originalEnabled) target["Enable"] = Enabled;
        if (ManualControl != _originalManualControl) target["ManualControl"] = ManualControl;
        if (ManualPercent != _originalManualPercent)
        {
            FanControlProfileDocument.PercentInRange(ManualPercent);
            target["ManualControlValue"] = ManualPercent;
        }
    }
}
