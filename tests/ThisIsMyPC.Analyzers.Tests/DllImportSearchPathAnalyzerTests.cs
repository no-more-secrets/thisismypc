using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ThisIsMyPC.Analyzers;

namespace ThisIsMyPC.Analyzers.Tests;

public class DllImportSearchPathAnalyzerTests
{
    // LibraryImport requires .NET 7+ reference assemblies.
    private static readonly ReferenceAssemblies Net80 = ReferenceAssemblies.Net.Net80;

    // The LibraryImport source generator doesn't run in analyzer test compilations,
    // so partial methods produce CS8795. We include it in expected diagnostics.
    private static DiagnosticResult PartialMethodError(int line, int col, string method) =>
        DiagnosticResult.CompilerError("CS8795")
            .WithSpan(line, col, line, col + method.Length)
            .WithArguments($"NativeMethods.{method}()");

    /// <summary>
    /// LibraryImport without DefaultDllImportSearchPaths reports TIPC001.
    /// </summary>
    [Fact]
    public async Task LibraryImport_WithoutSearchPath_ReportsTIPC001()
    {
        const string source = """
            using System.Runtime.InteropServices;

            public static partial class NativeMethods
            {
                [LibraryImport("kernel32.dll")]
                public static partial nint {|#0:GetCurrentProcess|}();
            }
            """;

        var test = new CSharpAnalyzerTest<DllImportSearchPathAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net80,
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DllImportSearchPathAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithArguments("GetCurrentProcess", "LibraryImport"));
        test.ExpectedDiagnostics.Add(PartialMethodError(6, 32, "GetCurrentProcess"));

        await test.RunAsync();
    }

    /// <summary>
    /// DllImport without DefaultDllImportSearchPaths reports TIPC001.
    /// </summary>
    [Fact]
    public async Task DllImport_WithoutSearchPath_ReportsTIPC001()
    {
        const string source = """
            using System.Runtime.InteropServices;

            public static class NativeMethods
            {
                [DllImport("kernel32.dll")]
                public static extern nint {|#0:GetCurrentProcess|}();
            }
            """;

        var expected = new DiagnosticResult(DllImportSearchPathAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("GetCurrentProcess", "DllImport");

        await CSharpAnalyzerVerifier<DllImportSearchPathAnalyzer, DefaultVerifier>
            .VerifyAnalyzerAsync(source, expected);
    }

    /// <summary>
    /// LibraryImport WITH DefaultDllImportSearchPaths(System32) produces no diagnostic.
    /// </summary>
    [Fact]
    public async Task LibraryImport_WithSearchPath_NoDiagnostic()
    {
        const string source = """
            using System.Runtime.InteropServices;

            public static partial class NativeMethods
            {
                [LibraryImport("kernel32.dll")]
                [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
                public static partial nint GetCurrentProcess();
            }
            """;

        var test = new CSharpAnalyzerTest<DllImportSearchPathAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net80,
        };
        // Only CS8795 expected (no source generator in test); no TIPC001 (attribute is present).
        test.ExpectedDiagnostics.Add(PartialMethodError(7, 32, "GetCurrentProcess"));

        await test.RunAsync();
    }

    /// <summary>
    /// DllImport WITH DefaultDllImportSearchPaths(System32) produces no diagnostic.
    /// </summary>
    [Fact]
    public async Task DllImport_WithSearchPath_NoDiagnostic()
    {
        const string source = """
            using System.Runtime.InteropServices;

            public static class NativeMethods
            {
                [DllImport("kernel32.dll")]
                [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
                public static extern nint GetCurrentProcess();
            }
            """;

        await CSharpAnalyzerVerifier<DllImportSearchPathAnalyzer, DefaultVerifier>
            .VerifyAnalyzerAsync(source);
    }

    /// <summary>
    /// DefaultDllImportSearchPaths with a non-System32 value (e.g., ApplicationDirectory)
    /// still reports TIPC001 — the value must be System32 specifically.
    /// </summary>
    [Fact]
    public async Task DllImport_WithWrongSearchPathValue_ReportsTIPC001()
    {
        const string source = """
            using System.Runtime.InteropServices;

            public static class NativeMethods
            {
                [DllImport("kernel32.dll")]
                [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory)]
                public static extern nint {|#0:GetCurrentProcess|}();
            }
            """;

        var expected = new DiagnosticResult(DllImportSearchPathAnalyzer.DiagnosticId, DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("GetCurrentProcess", "DllImport");

        await CSharpAnalyzerVerifier<DllImportSearchPathAnalyzer, DefaultVerifier>
            .VerifyAnalyzerAsync(source, expected);
    }

    /// <summary>
    /// Method without P/Invoke attributes produces no diagnostic.
    /// </summary>
    [Fact]
    public async Task RegularMethod_NoDiagnostic()
    {
        const string source = """
            public static class Helpers
            {
                public static int Add(int a, int b) => a + b;
            }
            """;

        await CSharpAnalyzerVerifier<DllImportSearchPathAnalyzer, DefaultVerifier>
            .VerifyAnalyzerAsync(source);
    }

    /// <summary>
    /// Assembly-level DefaultDllImportSearchPaths(System32) covers methods without per-method attribute.
    /// </summary>
    [Fact]
    public async Task AssemblyLevelAttribute_CoversMethod_NoDiagnostic()
    {
        const string source = """
            using System.Runtime.InteropServices;

            [assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

            public static class NativeMethods
            {
                [DllImport("kernel32.dll")]
                public static extern nint GetCurrentProcess();
            }
            """;

        await CSharpAnalyzerVerifier<DllImportSearchPathAnalyzer, DefaultVerifier>
            .VerifyAnalyzerAsync(source);
    }
}
