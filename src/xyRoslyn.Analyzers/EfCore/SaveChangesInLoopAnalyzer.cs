using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace xyRoslyn.Analyzers.EfCore;

/// <summary>
/// Reports SaveChanges and SaveChangesAsync of an EF Core DbContext inside the body of a foreach or
/// await foreach loop (ef-core EF-011). Calls inside lambdas and local functions are not reported,
/// because the analyzer cannot know when they run.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SaveChangesInLoopAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.SaveChangesInLoop,
        title: "Save once per unit of work, not inside a foreach loop",
        messageFormat: "'{0}' is called inside a foreach loop; collect the changes in the loop and save once after it",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every SaveChanges call is a database round trip and a transaction of its own (ef-core EF-011). " +
                     "Add or change all entities inside the loop and call SaveChanges once after it.",
        helpLinkUri: HelpLinks.Base + DiagnosticIds.SaveChangesInLoop + ".md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            // Projects without EF Core are skipped entirely.
            var dbContextType = start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
            if (dbContextType is null)
                return;

            start.RegisterOperationAction(c => AnalyzeInvocation(c, dbContextType), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol dbContextType)
    {
        if (context.Operation is not IInvocationOperation invocation)
            return;

        var method = invocation.TargetMethod;
        if (method.Name is not ("SaveChanges" or "SaveChangesAsync"))
            return;

        // DbContext itself and own contexts count, including overrides and methods hidden with "new";
        // same-named methods on types outside the DbContext hierarchy do not.
        if (!AnalyzerHelpers.InheritsFromOrEquals(method.ContainingType, dbContextType))
            return;

        for (var parent = invocation.Parent; parent is not null; parent = parent.Parent)
        {
            // When a lambda or local function runs is unknown here, so the search stops at its boundary.
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return;

            // Only the loop body counts; a call in the collection expression runs once.
            if (parent is IForEachLoopOperation loop && loop.Body.Syntax.Span.Contains(invocation.Syntax.Span))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, AnalyzerHelpers.MethodName(invocation), method.Name));
                return;
            }
        }
    }
}
