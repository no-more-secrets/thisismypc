using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Integration.Tests.Fakes;
using ThisIsMyPC.Modules.Shell.Models;

namespace ThisIsMyPC.Integration.Tests.ViewModels;

public sealed class CustomVerbSectionViewModelTests
{
    private readonly FakePendingChangesService _pending = new();
    private readonly EnumerableFakeRegistry _registry = new();
    private readonly CustomVerbSectionViewModel _vm;

    public CustomVerbSectionViewModelTests()
    {
        _vm = new CustomVerbSectionViewModel(_pending, _registry);
    }

    private void SubmitCreate(string label, string command, string scopeChoice = "All files")
    {
        _vm.OpenCreateFormCommand.Execute(null);
        _vm.FormLabel = label;
        _vm.FormCommandLine = command;
        _vm.SelectedScopeChoice = scopeChoice;
        _vm.SubmitCommand.Execute(null);
    }

    private CustomVerbDefinition StagedDefinition(int groupIndex) =>
        CustomVerbDefinition.Deserialize(_pending.PendingGroups[groupIndex].Changes[0].AfterValue)!;

    [Fact]
    public void Submit_without_label_or_command_sets_error_and_stages_nothing()
    {
        _vm.OpenCreateFormCommand.Execute(null);
        _vm.FormLabel = "Only a label";
        _vm.SubmitCommand.Execute(null);

        Assert.NotEqual("", _vm.FormError);
        Assert.True(_vm.IsFormOpen);
        Assert.Empty(_pending.PendingGroups);
    }

    [Fact]
    public void Create_stages_a_shell_custom_verb_create_descriptor()
    {
        SubmitCreate("Open Terminal Here", "wt.exe -d \"%1\"", scopeChoice: "Folders");

        var change = Assert.Single(Assert.Single(_pending.PendingGroups).Changes);
        Assert.Equal(ChangeValueType.Shell_CustomVerb, change.ValueType);
        Assert.Equal(ChangeCategory.Create, change.Category);
        var definition = StagedDefinition(0);
        Assert.Equal("Directory", definition.Scope);
        Assert.Equal("open-terminal-here", definition.VerbId);
        Assert.False(_vm.IsFormOpen);
    }

    [Fact]
    public void Two_staged_creates_with_the_same_label_get_distinct_verb_ids()
    {
        SubmitCreate("Open Here", "wt.exe");
        SubmitCreate("Open Here", "cmd.exe");

        Assert.Equal(2, _pending.PendingGroups.Count);
        Assert.NotEqual(StagedDefinition(0).VerbId, StagedDefinition(1).VerbId);
        Assert.NotEqual(
            _pending.PendingGroups[0].Changes[0].SystemLocation,
            _pending.PendingGroups[1].Changes[0].SystemLocation);
    }

    [Fact]
    public void Restaging_the_same_entry_supersedes_the_earlier_staged_group()
    {
        var applied = new CustomVerbDefinition
        {
            Scope = "*",
            VerbId = "open-here",
            Label = "Open Here",
            Command = "wt.exe",
        };
        _registry.SetString(applied.KeyPath, "", applied.Label);
        _registry.SetString($@"{applied.KeyPath}\command", "", applied.Command);
        _vm.RefreshCommand.Execute(null);
        var entry = Assert.Single(_vm.Entries);

        // Delete, then edit the same entry: the edit must replace the delete,
        // not resurrect the entry after it.
        _vm.DeleteCommand.Execute(entry);
        _vm.BeginEditCommand.Execute(entry);
        _vm.FormCommandLine = "cmd.exe";
        _vm.SubmitCommand.Execute(null);

        var change = Assert.Single(Assert.Single(_pending.PendingGroups).Changes);
        Assert.Equal(ChangeCategory.Modify, change.Category);
        Assert.Equal("cmd.exe", CustomVerbDefinition.Deserialize(change.AfterValue)!.Command);
    }

    /// <summary>Read/write/enumerate fake — just enough registry for CustomVerbService.</summary>
    private sealed class EnumerableFakeRegistry : Core.Services.IRegistryService
    {
        private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

        public void SetString(string keyPath, string valueName, string value)
        {
            _strings[$@"{keyPath}\{valueName}"] = value;
            _keys.Add(keyPath);
        }

        public Core.Results.OperationResult<string> ReadString(string keyPath, string valueName) =>
            _strings.TryGetValue($@"{keyPath}\{valueName}", out var v)
                ? Core.Results.OperationResult<string>.Success(v)
                : Core.Results.OperationResult<string>.Failure("Not found", Core.Results.ErrorCategory.NotFound);

        public Core.Results.OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath)
        {
            var prefix = keyPath + "\\";
            var children = _keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => k[prefix.Length..])
                .Where(k => !k.Contains('\\'))
                .ToArray();
            return Core.Results.OperationResult<IReadOnlyList<string>>.Success(children);
        }

        public Core.Results.OperationResult<int> ReadDWord(string keyPath, string valueName) => Fail<int>();
        public Core.Results.OperationResult<string> ReadExpandString(string keyPath, string valueName) => ReadString(keyPath, valueName);
        public Core.Results.OperationResult<string[]> ReadMultiString(string keyPath, string valueName) => Fail<string[]>();
        public Core.Results.OperationResult<byte[]> ReadBinary(string keyPath, string valueName) => Fail<byte[]>();
        public Core.Results.OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value) => Ok();
        public Core.Results.OperationResult<bool> WriteDWord(string keyPath, string valueName, int value) => Ok();
        public Core.Results.OperationResult<bool> WriteString(string keyPath, string valueName, string value)
        {
            SetString(keyPath, valueName, value);
            return Ok();
        }
        public Core.Results.OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value) => WriteString(keyPath, valueName, value);
        public Core.Results.OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values) => Ok();
        public Core.Results.OperationResult<bool> DeleteValue(string keyPath, string valueName)
        {
            _strings.Remove($@"{keyPath}\{valueName}");
            return Ok();
        }
        public Core.Results.OperationResult<bool> DeleteKey(string keyPath, bool recursive = false)
        {
            _keys.Remove(keyPath);
            return Ok();
        }
        public Core.Results.OperationResult<bool> KeyExists(string keyPath) =>
            Core.Results.OperationResult<bool>.Success(_keys.Contains(keyPath));
        public Core.Results.OperationResult<bool> ValueExists(string keyPath, string valueName) =>
            Core.Results.OperationResult<bool>.Success(_strings.ContainsKey($@"{keyPath}\{valueName}"));
        public Core.Results.OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath) =>
            Core.Results.OperationResult<IReadOnlyList<string>>.Success([]);
        public Core.Results.OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName) => ReadString(keyPath, valueName);

        private static Core.Results.OperationResult<bool> Ok() => Core.Results.OperationResult<bool>.Success(true);
        private static Core.Results.OperationResult<T> Fail<T>() =>
            Core.Results.OperationResult<T>.Failure("Not found", Core.Results.ErrorCategory.NotFound);
    }
}
