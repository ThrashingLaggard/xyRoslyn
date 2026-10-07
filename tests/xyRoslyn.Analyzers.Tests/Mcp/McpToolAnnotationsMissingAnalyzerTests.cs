using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using xyRoslyn.Analyzers.Mcp;
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    xyRoslyn.Analyzers.Mcp.McpToolAnnotationsMissingAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace xyRoslyn.Analyzers.Tests.Mcp;

public sealed class McpToolAnnotationsMissingAnalyzerTests
{
    // Minimal stand-in for the attributes of ModelContextProtocol.Core.
    // The usings come first so that the test code appended after the stub can use them.
    private const string McpStub = """
        using ModelContextProtocol.Server;

        namespace ModelContextProtocol.Server
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class McpServerToolAttribute : System.Attribute
            {
                public string Name { get; set; }
                public string Title { get; set; }
                public bool ReadOnly { get; set; }
                public bool Destructive { get; set; }
                public bool Idempotent { get; set; }
                public bool OpenWorld { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class McpServerToolTypeAttribute : System.Attribute
            {
            }
        }

        """;

    [Fact]
    public Task Reports_a_tool_without_any_annotation() => Verify.VerifyAnalyzerAsync(McpStub + """
        [McpServerToolType]
        public static class OrderTools
        {
            [{|ROSA006:McpServerTool|}]
            public static string GetOrder(int id) => "order";
        }
        """);

    [Fact]
    public Task Reports_the_missing_annotations_in_declaration_order() => Verify.VerifyAnalyzerAsync(
        McpStub + """
            [McpServerToolType]
            public static class OrderTools
            {
                [{|#0:McpServerTool(Name = "get_order", ReadOnly = true)|}]
                public static string GetOrder(int id) => "order";
            }
            """,
        Verify.Diagnostic().WithLocation(0).WithArguments("GetOrder", "Destructive, Idempotent, OpenWorld"));

    [Theory]
    [InlineData("Destructive = false, Idempotent = true, OpenWorld = false")]
    [InlineData("ReadOnly = true, Idempotent = true, OpenWorld = false")]
    [InlineData("ReadOnly = true, Destructive = false, OpenWorld = false")]
    [InlineData("ReadOnly = true, Destructive = false, Idempotent = true")]
    public Task Reports_when_a_single_annotation_is_missing(string annotations) => Verify.VerifyAnalyzerAsync(McpStub + $$"""
        [McpServerToolType]
        public static class OrderTools
        {
            [{|ROSA006:McpServerTool({{annotations}})|}]
            public static string GetOrder(int id) => "order";
        }
        """);

    // Explicit false counts as decided; the values themselves are a review question.
    [Fact]
    public Task Does_not_report_a_tool_that_sets_all_four() => Verify.VerifyAnalyzerAsync(McpStub + """
        [McpServerToolType]
        public static class OrderTools
        {
            [McpServerTool(Name = "get_order", Title = "Bestellung anzeigen",
                ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
            public static string GetOrder(int id) => "order";
        }
        """);

    // The inner namespace binds its own McpServerTool first.
    [Fact]
    public Task Does_not_report_a_same_named_attribute_from_another_namespace() => Verify.VerifyAnalyzerAsync(McpStub + """
        namespace Legacy.Tools
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class McpServerToolAttribute : System.Attribute
            {
            }

            public static class OrderTools
            {
                [McpServerTool]
                public static string GetOrder(int id) => "order";
            }
        }
        """);

    // Partial tool methods are common (XML-doc description generator); the attribute is reported once.
    [Fact]
    public Task Reports_a_partial_tool_method_once() => Verify.VerifyAnalyzerAsync(McpStub + """
        [McpServerToolType]
        public static partial class OrderTools
        {
            [{|ROSA006:McpServerTool|}]
            public static partial string GetOrder(int id);

            public static partial string GetOrder(int id) => "order";
        }
        """);

    // Incomplete code: the analyzer keeps working on a tool with an unresolved parameter type (no AD0001).
    [Fact]
    public Task Reports_a_tool_with_an_unresolved_parameter_type() => Verify.VerifyAnalyzerAsync(
        McpStub + """
            [McpServerToolType]
            public static class OrderTools
            {
                [{|ROSA006:McpServerTool|}]
                public static string GetOrder({|#0:OrderId|} id) => "order";
            }
            """,
        DiagnosticResult.CompilerError("CS0246").WithLocation(0).WithArguments("OrderId"));

    // Against the real package, so the stub above cannot drift from the real attribute unnoticed.
    [Fact]
    public Task Reports_a_tool_against_the_real_MCP_package() => new CSharpAnalyzerTest<McpToolAnnotationsMissingAnalyzer, DefaultVerifier>
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net100.AddPackages(
            [new PackageIdentity("ModelContextProtocol.Core", "2.2.0")]),
        TestCode = """
            using ModelContextProtocol.Server;

            [McpServerToolType]
            public static class OrderTools
            {
                [{|ROSA006:McpServerTool(Name = "get_order", ReadOnly = true)|}]
                public static string GetOrder(int id) => "order";
            }
            """,
    }.RunAsync(TestContext.Current.CancellationToken);
}
