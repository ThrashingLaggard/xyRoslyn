using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace xyRoslyn.Analyzers.Configuration;

/// <summary>
/// Reports IsDevelopment, IsProduction, IsStaging, IsEnvironment and reads of
/// IHostEnvironment.EnvironmentName outside composition code (configuration CFG-006).
/// Composition code is the program entry point (top-level statements or Main) and every method
/// with a parameter of a builder type: IServiceCollection, IHostApplicationBuilder, IHostBuilder,
/// IApplicationBuilder or IEndpointRouteBuilder, which covers Add{Feature} and Map{Feature} extensions.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EnvironmentQueryInBusinessCodeAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> EnvironmentChecks =
        ImmutableHashSet.Create("IsDevelopment", "IsProduction", "IsStaging", "IsEnvironment");

    private static readonly ImmutableArray<string> CompositionTypeNames = ImmutableArray.Create(
        "Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "Microsoft.Extensions.Hosting.IHostApplicationBuilder",
        "Microsoft.Extensions.Hosting.IHostBuilder",
        "Microsoft.AspNetCore.Builder.IApplicationBuilder",
        "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder");

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.EnvironmentQueryInBusinessCode,
        title: "Read configuration values instead of asking for the environment",
        messageFormat: "'{0}' asks for the environment outside composition code; read a configuration value instead",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Business code gets a value such as Ordering:VatRate, not a question about the environment " +
                     "(configuration CFG-006). Only composition code such as Program.cs switches developer tools by environment.",
        helpLinkUri: HelpLinks.Base + DiagnosticIds.EnvironmentQueryInBusinessCode + ".md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            var hostEnvironment = start.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.IHostEnvironment");
            if (hostEnvironment is null)
                return;

            var envExtensions = start.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.HostEnvironmentEnvExtensions");
            var compositionTypes = CompositionTypeNames
                .Select(start.Compilation.GetTypeByMetadataName)
                .Where(t => t is not null)
                .Select(t => t!)
                .ToImmutableArray();

            start.RegisterOperationAction(c =>
            {
                if (c.Operation is IInvocationOperation invocation
                    && EnvironmentChecks.Contains(invocation.TargetMethod.Name)
                    && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, envExtensions)
                    && !IsComposition(c.ContainingSymbol, compositionTypes))
                {
                    c.ReportDiagnostic(Diagnostic.Create(Rule, AnalyzerHelpers.MethodName(invocation), invocation.TargetMethod.Name));
                }
            }, OperationKind.Invocation);

            start.RegisterOperationAction(c =>
            {
                if (c.Operation is IPropertyReferenceOperation { Property.Name: "EnvironmentName" } property
                    && SymbolEqualityComparer.Default.Equals(property.Property.ContainingType, hostEnvironment)
                    && !IsAssignmentTarget(property)
                    && !IsComposition(c.ContainingSymbol, compositionTypes))
                {
                    c.ReportDiagnostic(Diagnostic.Create(Rule, AnalyzerHelpers.MemberName(property), "EnvironmentName"));
                }
            }, OperationKind.PropertyReference);
        });
    }

    private static bool IsAssignmentTarget(IOperation operation)
        => operation.Parent is IAssignmentOperation assignment && assignment.Target == operation;

    private static bool IsComposition(ISymbol symbol, ImmutableArray<INamedTypeSymbol> compositionTypes)
    {
        if (symbol is not IMethodSymbol method)
            return false;

        // Top-level statements compile to "<Main>$"; a classic Program.Main counts as well.
        if (method.Name == WellKnownMemberNames.TopLevelStatementsEntryPointMethodName || method is { Name: "Main", IsStatic: true })
            return true;

        return method.Parameters.Any(p => compositionTypes.Any(t => AnalyzerHelpers.IsOrImplements(p.Type, t)));
    }
}
