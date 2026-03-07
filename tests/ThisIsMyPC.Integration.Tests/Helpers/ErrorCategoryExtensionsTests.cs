using ThisIsMyPC.App.Helpers;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Integration.Tests.Helpers;

public class ErrorCategoryExtensionsTests
{
    [Theory]
    [InlineData(ErrorCategory.AccessDenied, "administrator")]
    [InlineData(ErrorCategory.NotFound, "not found")]
    [InlineData(ErrorCategory.ServiceUnavailable, "not available")]
    [InlineData(ErrorCategory.ProtectedByPolicy, "Group Policy")]
    [InlineData(ErrorCategory.RequiresRestart, "restart")]
    [InlineData(ErrorCategory.HardwareNotPresent, "hardware")]
    public void ToGuidance_ReturnsExpectedGuidanceForEachCategory(ErrorCategory category, string expectedSubstring)
    {
        var guidance = ErrorCategoryExtensions.ToGuidance(category);
        Assert.Contains(expectedSubstring, guidance);
    }
}
