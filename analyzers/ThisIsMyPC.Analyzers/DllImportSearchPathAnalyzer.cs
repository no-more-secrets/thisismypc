using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ThisIsMyPC.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DllImportSearchPathAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "TIPC001";

    // DllImportSearchPath.System32 = 0x800
    private const int DllImportSearchPathSystem32 = 2048;

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "P/Invoke declaration missing DLL search path restriction",
        messageFormat: "Method '{0}' uses [{1}] but is missing [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]. Add it to the method or the assembly.",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "All P/Invoke declarations must have [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] to prevent DLL search-order hijacking (NFR30).");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var searchPathType = compilationContext.Compilation.GetTypeByMetadataName(
                "System.Runtime.InteropServices.DefaultDllImportSearchPathsAttribute");

            if (searchPathType == null)
                return;

            bool assemblyHasSystem32 = HasValidAssemblySearchPath(
                compilationContext.Compilation, searchPathType);

            compilationContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeMethod(ctx, assemblyHasSystem32, searchPathType),
                SyntaxKind.MethodDeclaration);
        });
    }

    private static void AnalyzeMethod(
        SyntaxNodeAnalysisContext context,
        bool assemblyHasSystem32,
        INamedTypeSymbol searchPathType)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        // Fast syntax check: is this a P/Invoke method?
        string? pinvokeAttributeName = GetPInvokeAttributeName(method);
        if (pinvokeAttributeName == null)
            return;

        // Semantic check: does the method have a valid [DefaultDllImportSearchPaths(System32)]?
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(method);
        if (methodSymbol != null)
        {
            foreach (var attr in methodSymbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, searchPathType)
                    && IsSystem32SearchPath(attr))
                    return;
            }
        }

        // Cached assembly-level check
        if (assemblyHasSystem32)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            method.Identifier.GetLocation(),
            method.Identifier.Text,
            pinvokeAttributeName));
    }

    private static string? GetPInvokeAttributeName(MethodDeclarationSyntax method)
    {
        foreach (var attributeList in method.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = GetUnqualifiedAttributeName(attribute);
                if (name == "LibraryImport" || name == "LibraryImportAttribute")
                    return "LibraryImport";
                if (name == "DllImport" || name == "DllImportAttribute")
                    return "DllImport";
            }
        }
        return null;
    }

    private static string GetUnqualifiedAttributeName(AttributeSyntax attribute)
    {
        switch (attribute.Name)
        {
            case IdentifierNameSyntax id:
                return id.Identifier.Text;
            case QualifiedNameSyntax qn:
                return qn.Right.Identifier.Text;
            case AliasQualifiedNameSyntax alias:
                return alias.Name.Identifier.Text;
            default:
                return attribute.Name.ToString();
        }
    }

    private static bool IsSystem32SearchPath(AttributeData attr)
    {
        return attr.ConstructorArguments.Length > 0
            && attr.ConstructorArguments[0].Value is int value
            && value == DllImportSearchPathSystem32;
    }

    private static bool HasValidAssemblySearchPath(Compilation compilation, INamedTypeSymbol searchPathType)
    {
        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, searchPathType)
                && IsSystem32SearchPath(attr))
                return true;
        }
        return false;
    }
}
