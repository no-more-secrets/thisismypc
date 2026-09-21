using ThisIsMyPC.App.Services;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Hardware.Cooling;

namespace ThisIsMyPC.Integration.Tests.Hardware;

public sealed class CoolingActivationRequestTests
{
    [Fact]
    public async Task RequestUsesSavedAbsolutePathAndWindowFlag_WithoutCheckingWindowInstead()
    {
        using var fixture = new Fixture();
        var user = new FakeUser();
        var result = await new HardwareCompanionActions(user: user)
            .RequestFanControlProfileAsync(fixture.Store, "Quiet profile.json");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(fixture.Executable, user.Executable);
        Assert.Equal("-c \"" + fixture.ProfilePath + "\" -w", user.Arguments);
        Assert.True(fixture.Refreshed);
        Assert.Equal(Fixture.Profile, await File.ReadAllTextAsync(fixture.ProfilePath));
    }

    [Theory]
    [InlineData("../Quiet profile.json")]
    [InlineData("Quiet\" -e.json")]
    [InlineData("missing.json")]
    [InlineData("CACHE")]
    public async Task InvalidOrMissingProfileNeverLaunches(string name)
    {
        using var fixture = new Fixture();
        var user = new FakeUser();
        var result = await new HardwareCompanionActions(user: user).RequestFanControlProfileAsync(fixture.Store, name);
        Assert.False(result.IsSuccess);
        Assert.Null(user.Executable);
    }

    [Fact]
    public async Task UnsupportedProfileAndMissingExecutableNeverLaunch()
    {
        using var fixture = new Fixture();
        var user = new FakeUser();
        var actions = new HardwareCompanionActions(user: user);
        await File.WriteAllTextAsync(fixture.ProfilePath, Fixture.Profile.Replace("226", "999", StringComparison.Ordinal));
        Assert.False((await actions.RequestFanControlProfileAsync(fixture.Store, "Quiet profile.json")).IsSuccess);
        await File.WriteAllTextAsync(fixture.ProfilePath, Fixture.Profile);
        File.Delete(fixture.Executable);
        Assert.False((await actions.RequestFanControlProfileAsync(fixture.Store, "Quiet profile.json")).IsSuccess);
        Assert.Null(user.Executable);
    }

    [Fact]
    public async Task FreshConflictBlocksLaunchEvenWithValidSavedProfile()
    {
        using var fixture = new Fixture { Conflict = true };
        var user = new FakeUser();
        var result = await new HardwareCompanionActions(user: user).RequestFanControlProfileAsync(fixture.Store, "Quiet profile.json");
        Assert.False(result.IsSuccess);
        Assert.Contains("controls these devices", result.ErrorMessage);
        Assert.Null(user.Executable);
        Assert.True(fixture.Refreshed);
    }

    [Fact]
    public async Task CancelledPermissionPromptRemainsFailure()
    {
        using var fixture = new Fixture();
        var user = new FakeUser { Fail = true };
        var result = await new HardwareCompanionActions(user: user).RequestFanControlProfileAsync(fixture.Store, "Quiet profile.json");
        Assert.False(result.IsSuccess);
        Assert.Equal("Permission prompt cancelled.", result.ErrorMessage);
    }

    private sealed class FakeUser : IInteractiveUserContext
    {
        public bool IsCallerElevated => false;
        public InteractiveUser? Current => null;
        public string? Executable { get; private set; }
        public string? Arguments { get; private set; }
        public bool Fail { get; init; }
        public OperationResult<bool> LaunchAsUser(string applicationPath, string? arguments = null)
        {
            Executable = applicationPath;
            Arguments = arguments;
            return Fail ? OperationResult<bool>.Failure("Permission prompt cancelled.", ErrorCategory.ServiceUnavailable)
                : OperationResult<bool>.Success(true);
        }
        public OperationResult<T> RunAsUser<T>(Func<T> action) => OperationResult<T>.Success(action());
    }

    private sealed class Fixture : IHardwareFactsProvider, IDisposable
    {
        public const string Profile = """{"__VERSION__":"226","Main":{"Controls":[],"FanCurves":[]}}""";
        private readonly string _root = Directory.CreateTempSubdirectory("tipc-cooling-activation-").FullName;
        public string Executable => Path.Combine(_root, "FanControl.exe");
        public string ProfilePath => Path.Combine(_root, "Configurations", "Quiet profile.json");
        public FanControlProfileStore Store { get; }
        public bool Conflict { get; init; }
        public bool Refreshed { get; private set; }
        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Configurations"));
            File.WriteAllText(Executable, "test fixture, never executed");
            File.WriteAllText(ProfilePath, Profile);
            Store = new(this);
        }
        public HardwareDetectionSnapshot? Current
        {
            get
            {
                var facts = new ObservedHardwareFacts
                {
                    Companions = Conflict
                        ? [CompanionObservation.Running(CompanionApp.FanControl), CompanionObservation.Running(CompanionApp.GHelper, HardwareDomain.Cooling)]
                        : [CompanionObservation.Running(CompanionApp.FanControl)]
                };
                return new(facts, new HardwareSnapshot { Facts = facts },
                    new Dictionary<CompanionApp, string> { [CompanionApp.FanControl] = Executable }, [], DateTimeOffset.UtcNow);
            }
        }
        public event EventHandler? Changed { add { } remove { } }
        public Task<HardwareDetectionSnapshot> GetAsync(bool refresh = false, CancellationToken cancellationToken = default)
        { Refreshed |= refresh; return Task.FromResult(Current!); }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
