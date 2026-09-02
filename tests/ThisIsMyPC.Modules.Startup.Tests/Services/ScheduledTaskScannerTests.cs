using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;
using ThisIsMyPC.Modules.Startup.Services;
using ThisIsMyPC.Modules.Startup.Tests.Fakes;

namespace ThisIsMyPC.Modules.Startup.Tests.Services;

public class ScheduledTaskScannerTests : IDisposable
{
    private readonly FakeScheduledTaskService _tasks = new();
    private readonly string _storePath = Path.Combine(
        System.IO.Path.GetTempPath(), $"tipc-test-overrides-{Guid.NewGuid():N}.txt");

    private ScheduledTaskScanner CreateScanner()
        => new(_tasks, new TaskClassificationOverrideStore(_storePath));

    public void Dispose()
    {
        if (File.Exists(_storePath))
            File.Delete(_storePath);
    }

    [Theory]
    [InlineData(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", TaskClassification.Telemetry)]
    [InlineData(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", TaskClassification.CompatibilityDiagnostics)]
    [InlineData(@"\Microsoft\Windows\Defrag\ScheduledDefrag", TaskClassification.Maintenance)]
    [InlineData(@"\Microsoft\Windows\SomeNewFeature\MysteryTask", TaskClassification.Unknown)]
    public void Scan_ClassifiesByPath(string path, TaskClassification expected)
    {
        _tasks.AddTask(path, author: "Microsoft Corporation");

        var entry = Assert.Single(CreateScanner().Scan());

        Assert.Equal(expected, entry.Classification);
        Assert.False(entry.IsClassificationOverridden);
    }

    [Fact]
    public void Scan_OemAuthor_ClassifiedOem()
    {
        _tasks.AddTask(@"\HP\Support\HP Support Assistant Check", author: "HP Inc.");

        Assert.Equal(TaskClassification.Oem, Assert.Single(CreateScanner().Scan()).Classification);
    }

    [Fact]
    public void Scan_NonMicrosoftRootTask_ClassifiedUserCreated()
    {
        _tasks.AddTask(@"\MyBackupJob", author: "Sam");

        Assert.Equal(TaskClassification.UserCreated, Assert.Single(CreateScanner().Scan()).Classification);
    }

    [Fact]
    public void Scan_OverridePersistsAcrossScannerInstances()
    {
        _tasks.AddTask(@"\Microsoft\Windows\SomeNewFeature\MysteryTask", author: "Microsoft Corporation");
        var store = new TaskClassificationOverrideStore(_storePath);
        store.Set(@"\Microsoft\Windows\SomeNewFeature\MysteryTask", TaskClassification.Telemetry);

        var entry = Assert.Single(CreateScanner().Scan()); // fresh store instance reads the file

        Assert.Equal(TaskClassification.Telemetry, entry.Classification);
        Assert.True(entry.IsClassificationOverridden);
    }

    [Fact]
    public void Scan_CompanionTask_FlaggedWithDescription()
    {
        _tasks.AddTask(@"\Microsoft\Windows\AppxDeploymentClient\UCPD velocity", author: "Microsoft");

        var entry = Assert.Single(CreateScanner().Scan());

        Assert.True(entry.IsCompanionTask);
        Assert.Contains("UCPD", entry.CompanionDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_EnumerateFailure_SetsLastScanError()
    {
        _tasks.InjectFailure("EnumerateAll", "*");
        var scanner = CreateScanner();

        Assert.Empty(scanner.Scan());
        Assert.NotNull(scanner.LastScanError);
    }

    [Fact]
    public void ToStartupEntries_ProjectsOnlyLogonAndBootTasks()
    {
        _tasks.AddTask(@"\Vendor\LogonThing", triggers: ["LogonTrigger"]);
        _tasks.AddTask(@"\Vendor\BootThing", enabled: false, triggers: ["BootTrigger"]);
        _tasks.AddTask(@"\Vendor\DailyThing", triggers: ["CalendarTrigger"]);

        var startupEntries = ScheduledTaskScanner.ToStartupEntries(CreateScanner().Scan());

        Assert.Equal(2, startupEntries.Count);
        Assert.All(startupEntries, e => Assert.Equal(StartupSource.ScheduledTask, e.Source));
        Assert.False(startupEntries.Single(e => e.Name == "BootThing").IsEnabled);
    }

    [Fact]
    public void OverrideStore_RoundTripsAndIgnoresGarbageLines()
    {
        File.WriteAllLines(_storePath, [@"\A\B|Telemetry", "garbage-line", @"\C\D|NotAClassification"]);
        var store = new TaskClassificationOverrideStore(_storePath);

        Assert.Equal(TaskClassification.Telemetry, store.Get(@"\A\B"));
        Assert.Null(store.Get(@"\C\D"));

        store.Set(@"\E\F", TaskClassification.Oem);
        Assert.Equal(TaskClassification.Oem, new TaskClassificationOverrideStore(_storePath).Get(@"\E\F"));
    }

    [Fact]
    public void ParseDefinitionXml_ExtractsAuthorDescriptionTriggersAndTheExecAction()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>Contoso Ltd</Author>
                <Description>Does contoso things</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled></LogonTrigger>
                <CalendarTrigger />
              </Triggers>
              <Actions Context="Author">
                <Exec>
                  <Command>"C:\Program Files\Contoso\update.exe"</Command>
                  <Arguments>/silent</Arguments>
                  <WorkingDirectory>C:\Program Files\Contoso\</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        var definition = Interop.Com.Tasks.ScheduledTaskService.ParseDefinitionXml(xml);

        Assert.Equal("Contoso Ltd", definition.Author);
        Assert.Equal("Does contoso things", definition.Description);
        Assert.Equal(["LogonTrigger", "CalendarTrigger"], definition.TriggerTypes);
        Assert.Equal(@"""C:\Program Files\Contoso\update.exe""", definition.Command);
        Assert.Equal("/silent", definition.Arguments);
        Assert.Equal(@"C:\Program Files\Contoso\", definition.WorkingDirectory);
        Assert.Null(definition.ComHandlerClsid);
    }

    [Fact]
    public void ParseDefinitionXml_ComHandlerAction_GivesTheClassId()
    {
        const string xml = """
            <Task xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Actions>
                <ComHandler>
                  <ClassId>{2DEA658F-54C1-4227-AF9B-260AB5FC3543}</ClassId>
                </ComHandler>
              </Actions>
            </Task>
            """;

        var definition = Interop.Com.Tasks.ScheduledTaskService.ParseDefinitionXml(xml);

        Assert.Null(definition.Command);
        Assert.Equal("{2DEA658F-54C1-4227-AF9B-260AB5FC3543}", definition.ComHandlerClsid);
    }

    [Fact]
    public void ResolveIndirect_PlainText_IsReturnedAsIs()
    {
        Assert.Equal("Contoso Ltd", Interop.Com.Tasks.ScheduledTaskService.ResolveIndirect("Contoso Ltd"));
        Assert.Null(Interop.Com.Tasks.ScheduledTaskService.ResolveIndirect(null));
    }

    [Fact]
    public void ResolveIndirect_UnresolvableResource_KeepsTheReference()
    {
        const string reference = @"$(@%SystemRoot%\System32\no-such-file-here.dll,-103)";
        Assert.Equal(reference, Interop.Com.Tasks.ScheduledTaskService.ResolveIndirect(reference));
    }

    [Fact]
    public void ParseDefinitionXml_BrokenXml_IsEmpty()
    {
        var definition = Interop.Com.Tasks.ScheduledTaskService.ParseDefinitionXml("<Task><Actions>");

        Assert.Null(definition.Command);
        Assert.Empty(definition.TriggerTypes);
    }
}
