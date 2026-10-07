using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace xyRoslyn.Analyzers.Mcp;

/// <summary>
/// Reports [McpServerTool] attributes that do not set ReadOnly, Destructive, Idempotent and OpenWorld
/// explicitly (mcp-server MCP-005). Whether the values are true for the tool stays a review question;
/// the analyzer only makes sure that someone decided.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class McpToolAnnotationsMissingAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<string> Annotations = ImmutableArray.Create("ReadOnly", "Destructive", "Idempotent", "OpenWorld");

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.McpToolAnnotationsMissing,
        title: "Set every MCP tool annotation explicitly",
        messageFormat: "MCP tool '{0}' does not set {1} explicitly",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Clients decide from ReadOnly, Destructive, Idempotent and OpenWorld whether a tool needs a confirmation " +
                     "(mcp-server MCP-005). The protocol defaults assume the worst case, so every tool states all four.",
        helpLinkUri: HelpLinks.Base + DiagnosticIds.McpToolAnnotationsMissing + ".md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            var toolAttribute = start.Compilation.GetTypeByMetadataName("ModelContextProtocol.Server.McpServerToolAttribute");
            if (toolAttribute is null)
                return;

            start.RegisterSymbolAction(c => AnalyzeMethod(c, toolAttribute), SymbolKind.Method);
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, INamedTypeSymbol toolAttribute)
    {
        // A partial tool method is checked once, on its declaring part.
        if (context.Symbol is IMethodSymbol { PartialDefinitionPart: not null })
            return;

        foreach (var attribute in context.Symbol.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, toolAttribute))
                continue;

            var missing = Annotations.Where(name => attribute.NamedArguments.All(a => a.Key != name)).ToArray();
            if (missing.Length == 0)
                continue;

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                           ?? context.Symbol.Locations.FirstOrDefault()
                           ?? Location.None;
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, context.Symbol.Name, string.Join(", ", missing)));
        }
    }
}
