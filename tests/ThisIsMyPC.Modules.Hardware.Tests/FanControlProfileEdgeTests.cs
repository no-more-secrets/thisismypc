using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ThisIsMyPC.Modules.Hardware.Cooling;

namespace ThisIsMyPC.Modules.Hardware.Tests;

public sealed class FanControlProfileEdgeTests
{
    private const string Profile = """
        {"__VERSION__":"226","Main":{"Controls":[{"Identifier":"fan/0","Name":"Fan","NickName":"Pump","Enable":true,"ManualControl":false,"ManualControlValue":40,"Calibration":{"unknown":[0,22,91]},"PairedFanSensor":{"Identifier":"rpm/0"},"SelectedFanCurve":{"Name":"CPU","CommandMode":0,"MinimumTemperature":20,"MaximumTemperature":120,"Points":["29.8,29.1217777777778","85.6930172853179,100"],"privateField":"keep"}}],"FanCurves":[{"Name":"CPU","CommandMode":0,"MinimumTemperature":20,"MaximumTemperature":120,"Points":["29.8,29.1217777777778","85.6930172853179,100"],"SelectedTempSource":{"Identifier":"temperature/0"}},{"Name":"Fixed","CommandMode":0,"Percent":40},{"Name":"RPM","CommandMode":1,"Percent":1200,"Future":{"preserved":true}}]},"Sensors":{"Future":["keep",12]}}
        """;

    [Fact]
    public void UntouchedProfile_PreservesBomAndAllBytes()
    {
        byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(Profile)];
        Assert.Equal(bytes, FanControlProfileDocument.Parse(bytes).Serialize());
    }

    [Fact]
    public void NicknameEdit_PreservesFractionalPointsAndUnknownFields()
    {
        var document = FanControlProfileDocument.Parse(Encoding.UTF8.GetBytes(Profile));
        document.Controls[0].NickName = "AIO Pump";
        var actual = JsonNode.Parse(document.Serialize())!;
        var expected = JsonNode.Parse(Profile)!;
        expected["Main"]!["Controls"]![0]!["NickName"] = "AIO Pump";
        Assert.True(JsonNode.DeepEquals(expected, actual));
    }

    [Fact]
    public void GraphEdit_UpdatesNestedPointsAndKeepsNestedUnknownFields()
    {
        var document = FanControlProfileDocument.Parse(Encoding.UTF8.GetBytes(Profile));
        document.Curves[0].Points[0] = new(30, 0);
        var actual = JsonNode.Parse(document.Serialize())!;
        var nested = actual["Main"]!["Controls"]![0]!["SelectedFanCurve"]!;
        Assert.Equal("30,0", nested["Points"]![0]!.GetValue<string>());
        Assert.Equal("keep", nested["privateField"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(nested["Points"], actual["Main"]!["FanCurves"]![0]!["Points"]));
    }

    [Fact]
    public void RpmCurve_RemainsUntouchedWhenOtherCurveChanges()
    {
        var document = FanControlProfileDocument.Parse(Encoding.UTF8.GetBytes(Profile));
        Assert.Equal(FanControlCurveKind.Unsupported, document.Curves[2].Kind);
        document.Curves[1].Percent = 50;
        var actual = JsonNode.Parse(document.Serialize())!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Profile)!["Main"]!["FanCurves"]![2], actual["Main"]!["FanCurves"]![2]));
    }

    [Fact]
    public void GraphEncoding_UsesInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var document = FanControlProfileDocument.Parse(Encoding.UTF8.GetBytes(Profile));
            document.Curves[0].Points[0] = new(30.5m, 12.5m);
            var actual = JsonNode.Parse(document.Serialize())!;
            Assert.Equal("30.5,12.5", actual["Main"]!["FanCurves"]![0]!["Points"]![0]!.GetValue<string>());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(19, 50)]
    [InlineData(121, 50)]
    [InlineData(30, -1)]
    [InlineData(30, 101)]
    [InlineData(90, 50)]
    public void GraphRejectsOutOfRangeOrUnorderedPoints(int temperature, int percent)
    {
        var document = FanControlProfileDocument.Parse(Encoding.UTF8.GetBytes(Profile));
        document.Curves[0].Points[0] = new(temperature, percent);
        Assert.Throws<FormatException>(() => document.Serialize());
    }

    [Fact]
    public void DuplicateJsonProperty_IsRefusedBeforeEditing()
    {
        var invalid = Profile.Replace("\"Enable\":true", "\"Enable\":true,\"Enable\":false", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => FanControlProfileDocument.Parse(Encoding.UTF8.GetBytes(invalid)));
    }
}
