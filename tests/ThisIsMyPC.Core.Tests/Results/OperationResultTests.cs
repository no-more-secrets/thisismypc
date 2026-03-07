using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Tests.Results;

public class OperationResultTests
{
    [Fact]
    public void Success_CreatesResultWithIsSuccessTrueAndValueSet()
    {
        var result = OperationResult<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorCategory);
    }

    [Fact]
    public void Failure_CreatesResultWithIsSuccessFalseAndErrorDetails()
    {
        var result = OperationResult<int>.Failure("not found", ErrorCategory.NotFound);

        Assert.False(result.IsSuccess);
        Assert.Equal("not found", result.ErrorMessage);
        Assert.Equal(ErrorCategory.NotFound, result.ErrorCategory);
    }

    [Fact]
    public void Failure_ValueIsDefaultWhenFailure()
    {
        var result = OperationResult<string>.Failure("error", ErrorCategory.AccessDenied);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
    }
}
