using System.Text;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Hardware.Cooling;
using ThisIsMyPC.Modules.Hardware.Tests.Fakes;

namespace ThisIsMyPC.Modules.Hardware.Tests;

public sealed class FanControlProfileStoreTests : IDisposable
{
    private const string Profile = """
        {"__VERSION__":"226","Main":{"Controls":[{"Identifier":"fan/1","Name":"Fan 1","NickName":"Case fans","Enable":true,"ManualControl":false,"ManualControlValue":40,"Calibration":{"20":600},"SelectedFanCurve":{"Name":"Flat","CommandMode":0,"Percent":40}}],"FanCurves":[{"Name":"Flat","CommandMode":0,"Percent":40}]},"Sensors":{"Unknown":123}}
        """;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tipc-fancontrol-" + Guid.NewGuid().ToString("N"));
    private readonly FanControlProfileStore _store;
    private readonly CoolingModule _module;

    public FanControlProfileStoreTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Configurations"));
        File.WriteAllText(Path.Combine(_root, "Configurations", "original.json"), Profile);
        var facts = new FakeHardwareFactsProvider(ObservedHardwareFacts.Empty,
            new Dictionary<CompanionApp, string> { [CompanionApp.FanControl] = Path.Combine(_root, "FanControl.exe") });
        _module = new CoolingModule(facts, deletion: new TestOnlyDeletion());
        _store = _module.Profiles;
    }

    private async Task<ChangeDescriptor> EditAsync(string target = "copy.json", bool replace = false)
    {
        var loaded = await _store.ReadAsync("original.json");
        Assert.True(loaded.IsSuccess, loaded.ErrorMessage);
        loaded.Value!.Document.Controls[0].NickName = "New fan name";
        var staged = await _store.BuildChangeAsync(loaded.Value, target, replace);
        Assert.True(staged.IsSuccess, staged.ErrorMessage);
        return Assert.Single(staged.Value!.Changes);
    }

    [Fact]
    public async Task CopyApplyUndoRedo_UsesExactBeforeBytes()
    {
        var change = await EditAsync();
        Assert.False(File.Exists(Path.Combine(_root, "Configurations", "copy.json")));
        var applied = await _module.ApplyChangeAsync(change);
        Assert.True(applied.IsSuccess, applied.ErrorMessage);
        Assert.Contains("New fan name", await File.ReadAllTextAsync(change.SystemLocation));
        Assert.Equal(Profile, await File.ReadAllTextAsync(Path.Combine(_root, "Configurations", "original.json")));
        var undone = await _module.RevertChangeAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue });
        Assert.True(undone.IsSuccess, undone.ErrorMessage);
        Assert.False(File.Exists(change.SystemLocation));
        Assert.True((await _module.ApplyChangeAsync(change)).IsSuccess);
    }

    [Fact]
    public async Task ReplaceUndo_RestoresOriginalFormattingByteForByte()
    {
        var change = await EditAsync("original.json", replace: true);
        Assert.True((await _store.ApplyAsync(change)).IsSuccess);
        Assert.True((await _store.ApplyAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.Equal(Encoding.UTF8.GetBytes(Profile), await File.ReadAllBytesAsync(change.SystemLocation));
    }

    [Fact]
    public async Task PendingChanges_RollsBackACopyWhenNextChangeFails()
    {
        var change = await EditAsync();
        var queue = new PendingChangesService();
        queue.Stage(new ChangeGroup
        {
            GroupId = "test", DisplayName = "test", Description = "test",
            Changes = [change, change with { SettingId = "fail" }],
        });
        var result = await queue.ApplyAllAsync(
            item => item.SettingId == "fail"
                ? Task.FromResult(OperationResult<bool>.Failure("Expected test failure", ErrorCategory.ServiceUnavailable))
                : _module.ApplyChangeAsync(item), _module.RevertChangeAsync);
        Assert.Empty(result.Applied);
        Assert.False(File.Exists(change.SystemLocation));
    }

    [Fact]
    public async Task Apply_RefusesConcurrentEdit()
    {
        var change = await EditAsync("original.json", replace: true);
        await File.WriteAllTextAsync(change.SystemLocation, Profile + " ");
        Assert.False((await _store.ApplyAsync(change)).IsSuccess);
        Assert.Equal(Profile + " ", await File.ReadAllTextAsync(change.SystemLocation));
    }

    [Fact]
    public async Task Undo_RefusesChangedCopy()
    {
        var change = await EditAsync();
        Assert.True((await _store.ApplyAsync(change)).IsSuccess);
        await File.WriteAllTextAsync(change.SystemLocation, Profile);
        Assert.False((await _store.ApplyAsync(change with { BeforeValue = change.AfterValue!, AfterValue = change.BeforeValue })).IsSuccess);
        Assert.Equal(Profile, await File.ReadAllTextAsync(change.SystemLocation));
    }

    [Fact]
    public async Task Apply_RefusesFileLockedByFanControl()
    {
        var change = await EditAsync("original.json", replace: true);
        using var locked = new FileStream(change.SystemLocation, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.False((await _store.ApplyAsync(change)).IsSuccess);
    }

    [Fact]
    public async Task Stage_RefusesChangedSourceAndExistingCopy()
    {
        var loaded = (await _store.ReadAsync("original.json")).Value!;
        await File.WriteAllTextAsync(Path.Combine(_root, "Configurations", "copy.json"), Profile);
        Assert.False((await _store.BuildChangeAsync(loaded, "copy.json")).IsSuccess);
        Assert.False((await _store.BuildChangeAsync(loaded, "original.json")).IsSuccess);
        await File.WriteAllTextAsync(Path.Combine(_root, "Configurations", "original.json"), Profile + " ");
        Assert.False((await _store.BuildChangeAsync(loaded, "new.json")).IsSuccess);
    }

    [Theory]
    [InlineData("../escaped.json")]
    [InlineData("..\\escaped.json")]
    [InlineData("C:\\escaped.json")]
    [InlineData("x.json:stream")]
    [InlineData("NUL.json")]
    [InlineData("bad.txt")]
    public async Task Names_CannotEscapeConfigurationDirectory(string name)
    {
        var loaded = (await _store.ReadAsync("original.json")).Value!;
        Assert.False((await _store.BuildChangeAsync(loaded, name)).IsSuccess);
    }

    [Fact]
    public async Task Apply_RefusesChangedLocationAndInvalidPayload()
    {
        var change = await EditAsync();
        Assert.False((await _store.ApplyAsync(change with { SystemLocation = Path.Combine(_root, "other.json") })).IsSuccess);
        Assert.False((await _store.ApplyAsync(change with { AfterValue = "not base64" })).IsSuccess);
        Assert.False(File.Exists(change.SystemLocation));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // Single-threaded test fake only. Production deletion must keep an exclusive native handle open.
    private sealed class TestOnlyDeletion : IComparedFileDeletionService
    {
        public OperationResult<bool> DeleteIfMatches(string path, byte[] expected)
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
                return OperationResult<bool>.Failure("Changed file", ErrorCategory.ServiceUnavailable);
            File.Delete(path);
            return OperationResult<bool>.Success(true);
        }
    }
}
