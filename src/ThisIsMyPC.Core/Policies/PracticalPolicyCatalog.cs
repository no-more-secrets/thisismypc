using System.Collections.Immutable;

namespace ThisIsMyPC.Core.Policies;

/// <summary>First policy tranche: promotions, tailored experiences, and Search. No recommended values are implied.</summary>
public static class PracticalPolicyCatalog
{
    private const string CloudKey = @"Software\Policies\Microsoft\Windows\CloudContent";
    private const string ExperienceDocs = "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience";

    public static ImmutableArray<PolicyDefinition> Definitions { get; } =
    [
        Cloud("DisableWindowsConsumerFeatures", "Turn off Microsoft consumer experiences", PolicyScope.Machine),
        Cloud("DisableSoftLanding", "Do not show Windows tips", PolicyScope.Machine),
        Cloud("DisableThirdPartySuggestions", "Turn off third-party suggestions", PolicyScope.User),
        Cloud("DisableCloudOptimizedContent", "Turn off cloud optimized content", PolicyScope.Machine),
        Cloud("DisableConsumerAccountStateContent", "Turn off cloud consumer account state content", PolicyScope.Machine),
        Cloud("DisableTailoredExperiencesWithDiagnosticData", "Do not use diagnostic data for tailored experiences", PolicyScope.User,
            "Advertising & Tracking"),
        new("AllowSearchHighlights", "Allow search highlights", "Windows Annoyances", "Bing Search & Edge",
            @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "EnableDynamicContentInWSB",
            [PolicyScope.Machine], 1, 0, "Search.admx", "AllowSearchHighlights",
            "https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-search#allowsearchhighlights"),
    ];

    private static PolicyDefinition Cloud(string id, string name, PolicyScope scope, string section = "Nag Screens & Suggestions") =>
        new(id, name, "Windows Annoyances", section, CloudKey, id, [scope], 1, 0, "CloudContent.admx", id, ExperienceDocs);
}
