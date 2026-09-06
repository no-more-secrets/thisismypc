using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Enforcement;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Changes;

/// <summary>
/// The shared routing rule: Enforcement != null goes through IEnforcementExecutor,
/// null goes straight to the supplied delegate, and nothing else is inferred.
/// </summary>
public sealed class ReversibleChangeExecutorTests
{
    private static ChangeDescriptor Change(SettingEnforcement? enforcement = null) => new()
    {
        ModuleId = "TestModule",
        SettingId = "setting",
        DisplayName = "Setting",
        SystemLocation = @"HKCU\Test\Value",
        BeforeValue = "0",
        AfterValue = "1",
        BeforeDisplay = "0",
        AfterDisplay = "1",
        ValueType = ChangeValueType.Registry_DWord,
        Category = ChangeCategory.Modify,
        Enforcement = enforcement,
    };

    private static SettingEnforcement Enforced() => new() { CompanionServices = ["WSearch"] };

    private static Func<ChangeDescriptor, Task<OperationResult<bool>>> Recording(List<ChangeDescriptor> into) =>
        c =>
        {
            into.Add(c);
            return Task.FromResult(OperationResult<bool>.Success(true));
        };

    [Fact]
    public async Task Null_enforcement_calls_the_delegate_and_never_the_enforcement_executor()
    {
        var enforcement = new FakeEnforcementExecutor();
        var executor = new ReversibleChangeExecutor(enforcement);
        var applied = new List<ChangeDescriptor>();
        var reverted = new List<ChangeDescriptor>();

        var apply = await executor.ApplyAsync(Change(), Recording(applied));
        var revert = await executor.RevertAsync(Change(), Recording(reverted));

        Assert.True(apply.IsSuccess);
        Assert.True(revert.IsSuccess);
        Assert.Single(applied);
        Assert.Single(reverted);
        Assert.Empty(enforcement.ExecutedChanges);
        Assert.Empty(enforcement.RevertedChanges);
    }

    [Fact]
    public async Task Enforced_apply_goes_through_ExecuteAsync_with_the_callers_delegate()
    {
        var enforcement = new FakeEnforcementExecutor { InvokePrimary = true };
        var executor = new ReversibleChangeExecutor(enforcement);
        var applied = new List<ChangeDescriptor>();

        var result = await executor.ApplyAsync(Change(Enforced()), Recording(applied));

        Assert.True(result.IsSuccess);
        Assert.Single(enforcement.ExecutedChanges);
        Assert.Empty(enforcement.RevertedChanges);
        Assert.Single(applied);
    }

    [Fact]
    public async Task Enforced_revert_goes_through_RevertAsync_with_the_callers_delegate()
    {
        var enforcement = new FakeEnforcementExecutor { InvokePrimary = true };
        var executor = new ReversibleChangeExecutor(enforcement);
        var reverted = new List<ChangeDescriptor>();

        var result = await executor.RevertAsync(Change(Enforced()), Recording(reverted));

        Assert.True(result.IsSuccess);
        Assert.Single(enforcement.RevertedChanges);
        Assert.Empty(enforcement.ExecutedChanges);
        Assert.Single(reverted);
    }

    [Fact]
    public async Task Enforcement_failure_becomes_a_failed_result_with_its_message_and_category()
    {
        var enforcement = new FakeEnforcementExecutor
        {
            NextExecuteResult = new EnforcementResult
            {
                IsSuccess = false,
                ErrorMessage = "companion refused",
                ErrorCategory = ErrorCategory.AccessDenied,
            },
        };
        var executor = new ReversibleChangeExecutor(enforcement);

        var result = await executor.ApplyAsync(Change(Enforced()), Recording([]));

        Assert.False(result.IsSuccess);
        Assert.Equal("companion refused", result.ErrorMessage);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
    }

    [Fact]
    public async Task Enforcement_failure_without_a_message_uses_the_direction_fallback()
    {
        var enforcement = new FakeEnforcementExecutor
        {
            NextExecuteResult = new EnforcementResult { IsSuccess = false },
            NextRevertResult = new EnforcementResult { IsSuccess = false },
        };
        var executor = new ReversibleChangeExecutor(enforcement);

        var apply = await executor.ApplyAsync(Change(Enforced()), Recording([]));
        var revert = await executor.RevertAsync(Change(Enforced()), Recording([]));

        Assert.Equal("Enforcement execution failed", apply.ErrorMessage);
        Assert.Equal(ErrorCategory.ServiceUnavailable, apply.ErrorCategory);
        Assert.Equal("Enforcement revert failed", revert.ErrorMessage);
    }

    [Fact]
    public async Task Enforced_change_without_an_enforcement_executor_fails_and_never_calls_the_delegate()
    {
        var executor = new ReversibleChangeExecutor();
        var applied = new List<ChangeDescriptor>();

        Assert.False(executor.CanExecute(Change(Enforced())));
        var result = await executor.ApplyAsync(Change(Enforced()), Recording(applied));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCategory.ServiceUnavailable, result.ErrorCategory);
        Assert.Contains("requires enforcement", result.ErrorMessage);
        Assert.Empty(applied);
    }

    [Fact]
    public void CanExecute_is_true_for_plain_changes_with_or_without_an_enforcement_executor()
    {
        Assert.True(new ReversibleChangeExecutor().CanExecute(Change()));
        Assert.True(new ReversibleChangeExecutor(new FakeEnforcementExecutor()).CanExecute(Change()));
        Assert.True(new ReversibleChangeExecutor(new FakeEnforcementExecutor()).CanExecute(Change(Enforced())));
    }

    [Fact]
    public async Task Delegate_failure_passes_through_unchanged()
    {
        var executor = new ReversibleChangeExecutor();

        var result = await executor.ApplyAsync(
            Change(),
            _ => Task.FromResult(OperationResult<bool>.Failure("write refused", ErrorCategory.AccessDenied)));

        Assert.False(result.IsSuccess);
        Assert.Equal("write refused", result.ErrorMessage);
        Assert.Equal(ErrorCategory.AccessDenied, result.ErrorCategory);
    }

    [Fact]
    public async Task Enforcement_executor_receives_no_cancellation_token_as_before_the_extraction()
    {
        var enforcement = new TokenObservingExecutor();
        var executor = new ReversibleChangeExecutor(enforcement);

        await executor.ApplyAsync(Change(Enforced()), Recording([]));
        await executor.RevertAsync(Change(Enforced()), Recording([]));

        Assert.Equal([CancellationToken.None, CancellationToken.None], enforcement.Tokens);
    }

    [Fact]
    public async Task Delegate_exceptions_propagate_unwrapped()
    {
        var executor = new ReversibleChangeExecutor();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ApplyAsync(Change(), _ => throw new InvalidOperationException("module blew up")));
    }

    [Fact]
    public async Task Enforcement_executor_exceptions_propagate_unwrapped()
    {
        var executor = new ReversibleChangeExecutor(new ThrowingExecutor());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            executor.ApplyAsync(Change(Enforced()), Recording([])));
    }

    private sealed class TokenObservingExecutor : IEnforcementExecutor
    {
        public List<CancellationToken> Tokens { get; } = [];

        public Task<EnforcementResult> ExecuteAsync(
            ChangeDescriptor change,
            Func<ChangeDescriptor, Task<OperationResult<bool>>> applyPrimary,
            CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return Task.FromResult(new EnforcementResult { IsSuccess = true });
        }

        public Task<EnforcementResult> RevertAsync(
            ChangeDescriptor change,
            Func<ChangeDescriptor, Task<OperationResult<bool>>> revertPrimary,
            CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return Task.FromResult(new EnforcementResult { IsSuccess = true });
        }
    }

    private sealed class ThrowingExecutor : IEnforcementExecutor
    {
        public Task<EnforcementResult> ExecuteAsync(
            ChangeDescriptor change,
            Func<ChangeDescriptor, Task<OperationResult<bool>>> applyPrimary,
            CancellationToken cancellationToken = default)
            => throw new OperationCanceledException();

        public Task<EnforcementResult> RevertAsync(
            ChangeDescriptor change,
            Func<ChangeDescriptor, Task<OperationResult<bool>>> revertPrimary,
            CancellationToken cancellationToken = default)
            => throw new OperationCanceledException();
    }
}
