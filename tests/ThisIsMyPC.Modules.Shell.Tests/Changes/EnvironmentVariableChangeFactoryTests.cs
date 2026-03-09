using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Modules.Shell.Changes;
using ThisIsMyPC.Modules.Shell.Services;

namespace ThisIsMyPC.Modules.Shell.Tests.Changes;

public sealed class EnvironmentVariableChangeFactoryTests
{
    [Fact]
    public void CreateModify_produces_correct_descriptor()
    {
        var change = EnvironmentVariableChangeFactory.CreateModify(
            "TESTVAR", "old-value", "new-value", "user", EnvironmentVariableReader.UserEnvKeyPath);

        Assert.Equal("Environment", change.ModuleId);
        Assert.Equal("env-user-testvar", change.SettingId);
        Assert.Equal("Environment variable: TESTVAR", change.DisplayName);
        Assert.Equal(@"HKCU\Environment\TESTVAR", change.SystemLocation);
        Assert.Equal("old-value", change.BeforeValue);
        Assert.Equal("new-value", change.AfterValue);
        Assert.Equal(ChangeValueType.Environment_Variable, change.ValueType);
        Assert.Equal(ChangeCategory.Modify, change.Category);
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
    }

    [Fact]
    public void CreateModify_system_scope_uses_correct_settingId()
    {
        var change = EnvironmentVariableChangeFactory.CreateModify(
            "TESTVAR", "old", "new", "system", EnvironmentVariableReader.SystemEnvKeyPath);

        Assert.Equal("env-system-testvar", change.SettingId);
        Assert.Equal(@"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment\TESTVAR", change.SystemLocation);
    }

    [Fact]
    public void CreateAdd_produces_create_descriptor()
    {
        var change = EnvironmentVariableChangeFactory.CreateAdd(
            "NEW_VAR", "some-value", "user", EnvironmentVariableReader.UserEnvKeyPath);

        Assert.Equal("Environment", change.ModuleId);
        Assert.Equal("env-user-new_var", change.SettingId);
        Assert.Equal(ChangeCategory.Create, change.Category);
        Assert.Equal("", change.BeforeValue);
        Assert.Equal("some-value", change.AfterValue);
        Assert.Equal(ChangeValueType.Environment_Variable, change.ValueType);
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
    }

    [Fact]
    public void CreateAdd_displays_correct_names()
    {
        var change = EnvironmentVariableChangeFactory.CreateAdd(
            "MY_PATH", "/usr/bin", "user", EnvironmentVariableReader.UserEnvKeyPath);

        Assert.Contains("MY_PATH", change.DisplayName);
        Assert.Equal("(new)", change.BeforeDisplay);
        Assert.Equal("/usr/bin", change.AfterDisplay);
    }

    [Fact]
    public void CreateDelete_produces_delete_descriptor()
    {
        var change = EnvironmentVariableChangeFactory.CreateDelete(
            "OLD_VAR", "current-value", "user", EnvironmentVariableReader.UserEnvKeyPath);

        Assert.Equal("Environment", change.ModuleId);
        Assert.Equal("env-user-old_var", change.SettingId);
        Assert.Equal(ChangeCategory.Delete, change.Category);
        Assert.Equal("current-value", change.BeforeValue);
        Assert.Null(change.AfterValue);
        Assert.Equal(ChangeValueType.Environment_Variable, change.ValueType);
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
    }

    [Fact]
    public void CreatePathEdit_captures_full_path_values()
    {
        var oldPath = @"C:\Windows;C:\Windows\System32";
        var newPath = @"C:\Windows;C:\Windows\System32;C:\Tools";
        var diff = "+Added: C:\\Tools";

        var change = EnvironmentVariableChangeFactory.CreatePathEdit(
            "user", EnvironmentVariableReader.UserEnvKeyPath, oldPath, newPath, diff);

        Assert.Equal("Environment", change.ModuleId);
        Assert.Equal("env-user-path", change.SettingId);
        Assert.Equal(ChangeCategory.Modify, change.Category);
        Assert.Equal(oldPath, change.BeforeValue);
        Assert.Equal(newPath, change.AfterValue);
        Assert.Equal(ChangeValueType.Environment_Variable, change.ValueType);
        Assert.Equal(RestartRequirement.None, change.RestartRequirement);
    }

    [Fact]
    public void CreatePathEdit_system_scope_uses_correct_settingId()
    {
        var change = EnvironmentVariableChangeFactory.CreatePathEdit(
            "system", EnvironmentVariableReader.SystemEnvKeyPath, "A;B", "A;B;C", "+Added: C");

        Assert.Equal("env-system-path", change.SettingId);
    }

    [Fact]
    public void CreatePathEdit_uses_human_readable_diff_for_display()
    {
        var diff = "+Added: C:\\Tools\n-Removed: C:\\OldDir";

        var change = EnvironmentVariableChangeFactory.CreatePathEdit(
            "user", EnvironmentVariableReader.UserEnvKeyPath, "A;B", "C;D", diff);

        Assert.Equal(diff, change.BeforeDisplay);
        Assert.Equal(diff, change.AfterDisplay);
    }
}
