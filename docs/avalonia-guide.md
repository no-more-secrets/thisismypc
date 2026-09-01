# Avalonia Development Guide

Patterns and constraints for Avalonia UI, ViewModels, and XAML in this app. Read before touching either.

## Compiled Bindings

Compiled bindings are enabled globally in `ThisIsMyPC.App.csproj`:

```xml
<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
```

Every view must declare `x:DataType` at root level:

```xml
<Window x:DataType="vm:MainWindowViewModel" ...>
<UserControl x:DataType="vm:ReviewPanelViewModel" ...>
```

Every `DataTemplate` inside an `ItemsControl` must also declare `x:DataType`:

```xml
<DataTemplate x:DataType="vm:SidebarItemViewModel">
```

Shared row templates (`Templates/ToggleSettingRowTemplate.axaml`, `ChoiceSettingRowTemplate.axaml`, `MultiScopeRowTemplate.axaml`, `AdvancedExpanderTemplate.axaml`) are typed against interfaces such as `IToggleSettingRow` (`ViewModels/IToggleSettingRow.cs`) so their bindings compile too:

```xml
<DataTemplate x:Key="ToggleSettingRowTemplate" x:DataType="vm:IToggleSettingRow">
```

Row ViewModels that do not use an optional member return a constant that hides that part of the row. Never disable compiled bindings for a template: NativeAOT trimming breaks reflection bindings silently, and the AOT publish gate is zero trim warnings.

## Classes Binding

Avalonia's `Classes` property cannot be bound to a string. You cannot do `Classes="{Binding TintClass}"`. Instead, use boolean properties with the `Classes.xxx` syntax:

```xml
<Border Classes.pending-enable="{Binding IsEnableOrCreate}"
        Classes.pending-disable="{Binding IsDisableOrDelete}"
        Classes.pending-modify="{Binding IsModifyCategory}">
```

Backed by computed properties in the ViewModel:

```csharp
public bool IsEnableOrCreate => Category is ChangeCategory.Enable or ChangeCategory.Create;
public bool IsDisableOrDelete => Category is ChangeCategory.Disable or ChangeCategory.Delete;
public bool IsModifyCategory => Category is ChangeCategory.Modify;
```

Then styled via selectors in `Controls.axaml`:

```xml
<Style Selector="Border.pending-enable">
    <Setter Property="Background" Value="{DynamicResource SuccessMutedBrush}" />
</Style>
```

## Command Binding in DataTemplates

When inside a nested `DataTemplate`, the `DataContext` is the item, not the parent ViewModel. To reach a command on the parent Window's ViewModel:

```xml
<!-- From a DataTemplate inside an ItemsControl -->
<Button Command="{Binding $parent[Window].((vm:MainWindowViewModel)DataContext).NavigateToModuleCommand}"
        CommandParameter="{Binding}" />
```

To reach a command on a parent UserControl's ViewModel:

```xml
<!-- From ChangeHistoryView's DataTemplate -->
<Button Command="{Binding $parent[UserControl].((vm:ChangeHistoryViewModel)DataContext).RevertCommand}"
        CommandParameter="{Binding}" />
```

The `$parent[Type]` syntax is Avalonia-specific (WPF uses `RelativeSource FindAncestor`).

## Popup Constraints

Popups cannot be a direct sibling of other elements inside a `Border` (Border accepts only one child). Wrap in a `Panel`:

```xml
<!-- WRONG: Border can't have multiple children -->
<Border>
    <DockPanel ... />
    <Popup ... />     <!-- Error -->
</Border>

<!-- CORRECT: Panel allows multiple children -->
<Panel>
    <Border x:Name="ApplyBar" ... />
    <Popup PlacementTarget="{Binding #ApplyBar}"
           Placement="Top"
           IsOpen="{Binding IsReviewPanelOpen, Mode=TwoWay}"
           IsLightDismissEnabled="True"
           WindowManagerAddShadowHint="False">
        <views:ReviewPanelView DataContext="{Binding ReviewPanel}" />
    </Popup>
</Panel>
```

Key Popup properties:
- `PlacementTarget="{Binding #ApplyBar}"` -- `#Name` is Avalonia's element name binding syntax
- `IsLightDismissEnabled="True"` -- closes on outside click and ESC
- `WindowManagerAddShadowHint="False"` -- use custom border styling instead
- `IsOpen` must be `Mode=TwoWay` for light dismiss to update the ViewModel

## Thread Marshaling

Services like `PendingChangesService` use `ConfigureAwait(false)` internally. Their `PropertyChanged` events may fire on thread pool threads. ViewModels subscribed to these services must marshal to the UI thread:

```csharp
private void OnPendingChangesPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName is nameof(IPendingChangesService.PendingCount))
    {
        if (Dispatcher.UIThread.CheckAccess())
            PendingCount = _pendingChangesService.PendingCount;
        else
            Dispatcher.UIThread.Post(() => PendingCount = _pendingChangesService.PendingCount);
    }
}
```

Use `Dispatcher.UIThread.CheckAccess()` to test if already on the UI thread. Use `Dispatcher.UIThread.Post()` to dispatch.

For async methods that need to return to the UI thread, use `.ConfigureAwait(true)`:

```csharp
// In ViewModel (needs UI context after await)
await _changeHistoryService.InitializeAsync().ConfigureAwait(true);

// In service/library code (doesn't need UI context)
await connection.OpenAsync().ConfigureAwait(false);
```

## NativeAOT Constraints

These constraints affect every piece of code in the project:

1. **No reflection.** No `Type.GetType()`, no `Activator.CreateInstance()`. The template-generated `ViewLocator.cs` was removed for this reason.
2. **Explicit DI registration.** All services and modules registered by type in `App.axaml.cs ConfigureServices()`. No assembly scanning.
3. **String-typed values.** `ChangeDescriptor` uses `string BeforeValue/AfterValue` with a `ChangeValueType` enum discriminator. No `object?`, no boxing.
4. **Converters as singletons.** Custom `IValueConverter` implementations use a static `Instance` field instead of XAML instantiation:

```csharp
public sealed class SidebarWidthConverter : IValueConverter
{
    public static readonly SidebarWidthConverter Instance = new();
    // ...
}
```

Referenced in XAML via `x:Static`:

```xml
<Border Width="{Binding IsSidebarCollapsed, Converter={x:Static vm:SidebarWidthConverter.Instance}}" />
```

5. **Avalonia built-in converters** are NativeAOT-safe. Prefer these over custom converters:

```xml
<TextBlock IsVisible="{Binding StatusMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
<StackPanel IsVisible="{Binding CurrentContent, Converter={x:Static ObjectConverters.IsNull}}" />
```

6. **Suppress CA1515** in the App project -- Avalonia XAML requires public types. Already configured in `.csproj`.
7. **Disable DataAnnotationsValidationPlugin** on startup -- uses reflection. See `App.axaml.cs DisableAvaloniaDataAnnotationValidation()`.

## CommunityToolkit.Mvvm Conventions

All ViewModels inherit `ViewModelBase` which extends `ObservableObject`:

```csharp
public abstract class ViewModelBase : ObservableObject { }
public partial class MainWindowViewModel : ViewModelBase { }
```

The `partial` keyword is required for source generators.

### Observable Properties

```csharp
[ObservableProperty]
private int _pendingCount;

// Notify dependent computed properties when this changes
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(HasPendingChanges))]
[NotifyPropertyChangedFor(nameof(PendingCountText))]
[NotifyPropertyChangedFor(nameof(CanModifyPending))]
private int _pendingCount;

// Computed properties (not observable themselves, notified via the above)
public bool HasPendingChanges => PendingCount > 0;
public bool CanModifyPending => HasPendingChanges && !IsApplying;
```

### Relay Commands

```csharp
[RelayCommand]
private void ToggleSidebar() { IsSidebarCollapsed = !IsSidebarCollapsed; }

[RelayCommand]
private async Task ApplyAllAsync() { /* ... */ }
```

The toolkit generates `ToggleSidebarCommand` and `ApplyAllCommand` properties. XAML binds to these:

```xml
<Button Command="{Binding ToggleSidebarCommand}" />
<Button Command="{Binding ApplyAllCommand}" />
```

### Core vs App Layer

`ThisIsMyPC.Core` has NO CommunityToolkit.Mvvm dependency. Services in Core implement `INotifyPropertyChanged` manually. Only App-layer ViewModels use CommunityToolkit.

## Font and Theme Patterns

### Font Families

Defined in `Styles/Typography.axaml`:

```xml
<FontFamily x:Key="BodyFont">avares://ThisIsMyPC.App/Assets/Fonts#IBM Plex Sans</FontFamily>
<FontFamily x:Key="MonoFont">avares://ThisIsMyPC.App/Assets/Fonts#IBM Plex Mono</FontFamily>
<FontFamily x:Key="DisplayFont">avares://ThisIsMyPC.App/Assets/Fonts#IBM Plex Sans</FontFamily>
```

The `avares://` protocol references embedded font assets. Font files are in `Assets/Fonts/`: IBM Plex Sans, IBM Plex Mono, and OpenDyslexic (the accessibility swap driven by `Services/AccessibilityFontService.cs`). The `#FontFamilyName` suffix after the directory path is how Avalonia resolves the font family from the files in a folder.

Page titles use IBM Plex Sans Bold 28, not a separate brand face. Set `FontFamily="{DynamicResource BodyFont}"` on the Window root so all controls inherit it; the accessibility font swap replaces the resource at runtime.

### Theme Variants

`Styles/Theme.axaml` holds one `ResourceDictionary.ThemeDictionaries` with a `Dark` and a `Light` dictionary. Every key exists in both. Consumers reference brushes with `DynamicResource`, never `StaticResource`, so the running app restyles when `Services/ThemeService.cs` sets `Application.RequestedThemeVariant`. `App.axaml` requests `Dark` by default.

Background tiers, darkest to lightest in the dark variant:

| Tier | Key | Dark | Light | Usage |
|------|-----|------|-------|-------|
| Base | `BaseBrush` | `#1a1a2e` | `#f2f3f8` | Window background |
| Raised | `RaisedBrush` | `#242438` | `#e8eaf1` | Sidebar, apply bar, popups |
| Surface | `SurfaceBrush` | `#2d2d42` | `#ffffff` | Content area, cards |
| Overlay | `OverlayBrush` | `#383850` | `#dfe2ec` | Hover states, secondary buttons |

Add a new color to both dictionaries or the other variant throws at resource lookup.

### Text Opacity via Alpha Channel

Text brushes are alpha-channel values over the tier backgrounds, not flat grays, so text reads correctly on every tier. Dark uses white with alpha; light uses a near-black with alpha:

```xml
<!-- Dark -->
<SolidColorBrush x:Key="TextPrimaryBrush" Color="#FFFFFF" />
<SolidColorBrush x:Key="TextSecondaryBrush" Color="#CCFFFFFF" />
<!-- Light -->
<SolidColorBrush x:Key="TextPrimaryBrush" Color="#E014142A" />
<SolidColorBrush x:Key="TextSecondaryBrush" Color="#A614142A" />
```

### Semantic Colors

Each semantic color has a full and muted variant. Muted variants are for subtle background tinting. Both live in each theme dictionary:

```xml
<!-- Dark -->
<Color x:Key="SuccessColor">#4caf50</Color>       <!-- Text/icon -->
<Color x:Key="SuccessMutedColor">#2d4a2e</Color>  <!-- Background tint -->
<!-- Light -->
<Color x:Key="SuccessColor">#2e7d32</Color>
<Color x:Key="SuccessMutedColor">#d8ecd9</Color>
```

Available semantics: `Success` (green), `Warning` (amber), `Danger` (red), `Info` (cyan).

## Button Hover Styling

Avalonia's FluentTheme overrides `Background` on `Button:pointerover` via the internal `ContentPresenter` template part. To make custom hover backgrounds work, target the template:

```xml
<Style Selector="Button.sidebar-item:pointerover /template/ ContentPresenter">
    <Setter Property="Background" Value="{DynamicResource SidebarItemHoverBrush}" />
</Style>
```

The `/template/` selector pierces into the control's template to reach the `ContentPresenter`.

## Testing Patterns

ViewModels are tested as pure C# without the Avalonia runtime:

```csharp
[Fact]
public void PendingCount_UpdatesWhenChangeStaged()
{
    var service = new PendingChangesService();
    var vm = CreateViewModel(pendingChangesService: service);

    service.Stage(CreateTestChange());

    Assert.Equal(1, vm.PendingCount);
}
```

Test helpers create `ChangeDescriptor` instances with required fields:

```csharp
private static ChangeDescriptor CreateTestChange(
    ChangeCategory category = ChangeCategory.Enable) => new()
{
    ModuleId = "test",
    SettingId = "setting1",
    DisplayName = "Test Setting",
    SystemLocation = @"HKLM\Test",
    BeforeValue = "0",
    AfterValue = "1",
    BeforeDisplay = "Disabled",
    AfterDisplay = "Enabled",
    ValueType = ChangeValueType.Registry_DWord,
    Category = category,
};
```

SQLite repository tests use in-memory databases (`:memory:` connection string). Call `SqliteConnection.ClearAllPools()` in test Dispose to avoid connection pool locking.

## DI Registration Pattern

All registration happens in `App.axaml.cs ConfigureServices()`:

```csharp
private static void ConfigureServices(IServiceCollection services)
{
    // Modules (explicit, NativeAOT-safe -- no assembly scanning)
    services.AddSingleton<IModule, ShellModule>();
    services.AddSingleton<IModule, StartupModule>();
    services.AddSingleton<IModule, PowerModule>();

    // Core services
    services.AddSingleton<IPendingChangesService, PendingChangesService>();
    services.AddSingleton<ChangeHistoryRepository>();
    services.AddSingleton<IChangeHistoryService, ChangeHistoryService>();

    // App services
    services.AddSingleton<NavigationService>();

    // ViewModels
    services.AddSingleton<MainWindowViewModel>();
    services.AddSingleton<ReviewPanelViewModel>();
}
```

Every module follows the same pattern: one explicit `AddSingleton<IModule, ConcreteModule>()` call per module.

## Layout Patterns

### Sidebar + Content (Grid)

```xml
<Grid ColumnDefinitions="Auto,*">
    <Border Grid.Column="0" ... />    <!-- Sidebar: Auto width -->
    <DockPanel Grid.Column="1" ... /> <!-- Content: fills remaining -->
</Grid>
```

### Header + Footer + Content (DockPanel)

```xml
<DockPanel>
    <Border DockPanel.Dock="Top" ... />     <!-- Header -->
    <Panel DockPanel.Dock="Bottom" ... />   <!-- Footer (apply bar + popups) -->
    <Border ... />                          <!-- Content: fills remaining -->
</DockPanel>
```

### Overlay / Placeholder (Panel)

```xml
<Panel>
    <ContentControl Content="{Binding CurrentContent}" />
    <StackPanel IsVisible="{Binding CurrentContent, Converter={x:Static ObjectConverters.IsNull}}" ... />
</Panel>
```

## Common Suppressions

| Code | Reason | Scope |
|------|--------|-------|
| CA1515 | Avalonia XAML requires public types | App project |
| CA1724 | `App` class name conflicts with `ThisIsMyPC.App` namespace | App project |
| CA1707 | Architecture-prescribed underscore naming in `ChangeValueType` | Core project |
| CA1040 | Intentional stub interfaces (e.g., `IChangeHistoryService` before implementation) | Removed when stub is replaced |
| CA1816, CA1033 | `IModule` default `DisposeAsync` no-op | Core project |
| CA1000 | `OperationResult<T>` static factory methods | Core project |
| CA1707, CA1062 | Test naming conventions and null parameter checks | Test projects |
