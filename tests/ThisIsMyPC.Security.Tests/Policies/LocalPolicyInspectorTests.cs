using System.Xml.Linq;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Interop.Win32.Policies;
using ThisIsMyPC.Interop.Win32.Registry;

namespace ThisIsMyPC.Security.Tests.Policies;

public sealed class LocalPolicyInspectorTests
{
    [Fact, Trait("Category", "Diagnostic")]
    public void CatalogMatchesInstalledAdministrativeTemplates()
    {
        var templates = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "PolicyDefinitions");
        foreach (var definition in PracticalPolicyCatalog.Definitions)
        {
            var document = XDocument.Load(Path.Combine(templates, definition.AdmxFile));
            var ns = document.Root!.Name.Namespace;
            var policy = Assert.Single(document.Descendants(ns + "policy"), e => (string?)e.Attribute("name") == definition.AdmxPolicyId);
            Assert.Equal(definition.KeyPath, (string?)policy.Attribute("key"), ignoreCase: true);
            Assert.Equal(definition.ValueName, (string?)policy.Attribute("valueName"));
            Assert.Equal(definition.EnabledValue, (uint)policy.Element(ns + "enabledValue")!.Element(ns + "decimal")!.Attribute("value")!);
            Assert.Equal(definition.DisabledValue, (uint)policy.Element(ns + "disabledValue")!.Element(ns + "decimal")!.Attribute("value")!);
            Assert.Equal(Assert.Single(definition.Scopes).ToString(), (string?)policy.Attribute("class"));
            Assert.Null(policy.Element(ns + "elements"));
        }
    }

    [Fact, Trait("Category", "Diagnostic")]
    public void ReportSavedAndRegistryPolicyStatesWithoutChangingWindows()
    {
        var observations = new LocalPolicyInspector(new RegistryService()).ReadCurrent();
        Assert.Equal(PracticalPolicyCatalog.Definitions.Sum(d => d.Scopes.Length), observations.Length);
        foreach (var observation in observations)
        {
            Assert.NotEmpty(observation.Sources);
            Console.WriteLine($"{observation.Definition.Id} ({observation.Scope}): saved={observation.SavedState}, registry={observation.RegistryState}, comparison={observation.Comparison}");
        }
    }
}
