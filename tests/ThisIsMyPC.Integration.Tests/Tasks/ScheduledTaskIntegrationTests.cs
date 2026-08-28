using ThisIsMyPC.Interop.Com.Tasks;

namespace ThisIsMyPC.Integration.Tests.Tasks;

/// <summary>
/// Read-only checks against the live Task Scheduler — validates the raw
/// vtable interop (indices, VARIANT marshaling, BSTR handling). Never mutates.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ScheduledTaskIntegrationTests
{
    [Fact]
    public void EnumerateAll_ReturnsSystemTasks()
    {
        var result = new ScheduledTaskService().EnumerateAll();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotEmpty(result.Value!);
        Assert.All(result.Value!, t =>
        {
            Assert.False(string.IsNullOrEmpty(t.Name));
            Assert.StartsWith(@"\", t.Path, StringComparison.Ordinal);
        });
        // A default Windows install always has Microsoft tasks
        Assert.Contains(result.Value!, t => t.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase));
        // XML parsing should produce triggers for at least some tasks
        Assert.Contains(result.Value!, t => t.TriggerTypes.Count > 0);
    }

    [Fact]
    public void Query_FirstEnumeratedTask_RoundTrips()
    {
        var service = new ScheduledTaskService();
        var all = service.EnumerateAll();
        Assert.True(all.IsSuccess, all.ErrorMessage);
        var first = all.Value![0];

        var queried = service.Query(first.Path);

        Assert.True(queried.IsSuccess, queried.ErrorMessage);
        Assert.Equal(first.Path, queried.Value!.Path);
        Assert.Equal(first.Name, queried.Value.Name);
    }

    [Fact]
    public void Query_NonexistentTask_ReturnsNotFound()
    {
        var result = new ScheduledTaskService().Query(@"\ThisIsMyPC\DoesNotExist12345");

        Assert.False(result.IsSuccess);
        Assert.Equal(Core.Results.ErrorCategory.NotFound, result.ErrorCategory);
    }
}
