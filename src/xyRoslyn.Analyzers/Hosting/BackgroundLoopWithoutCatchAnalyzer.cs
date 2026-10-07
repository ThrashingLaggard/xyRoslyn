using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace xyRoslyn.Analyzers.Hosting;

/// <summary>
/// Reports an outermost loop in an override of BackgroundService.ExecuteAsync whose body has no
/// try statement that catches Exception (dependency-injection DI-016). Since .NET 6 an unhandled
/// exception there stops the whole host. Statements next to the try, such as a Task.Delay with the
/// stopping token, are allowed, so that cancellation still ends the loop.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BackgroundLoopWithoutCatchAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.BackgroundLoopWithoutCatch,
        title: "Catch errors per pass in a background service loop",
        messageFormat: "The '{0}' loop in ExecuteAsync has no try/catch for Exception; one failed pass stops the whole application",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An unhandled exception in BackgroundService.ExecuteAsync stops the host (dependency-injection DI-016). " +
                     "The loop body catches Exception per pass, logs it and continues; cancellation is let through with a filter.",
        helpLinkUri: HelpLinks.Base + DiagnosticIds.BackgroundLoopWithoutCatch + ".md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            var backgroundService = start.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.BackgroundService");
            var exception = start.Compilation.GetTypeByMetadataName("System.Exception");
            var executeAsync = backgroundService?.GetMembers("ExecuteAsync").OfType<IMethodSymbol>().FirstOrDefault();
            if (executeAsync is null || exception is null)
                return;

            start.RegisterOperationAction(c => AnalyzeLoop(c, executeAsync, exception), OperationKind.Loop);
        });
    }

    private static void AnalyzeLoop(OperationAnalysisContext context, IMethodSymbol executeAsync, INamedTypeSymbol exception)
    {
        if (context.Operation is not ILoopOperation loop || !Overrides(context.ContainingSymbol as IMethodSymbol, executeAsync))
            return;

        // Only the outermost loop of ExecuteAsync itself; inner loops sit inside its try.
        for (var parent = loop.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ILoopOperation or IAnonymousFunctionOperation or ILocalFunctionOperation)
                return;
        }

        if (HasCatchForException(loop.Body, exception))
            return;

        var keyword = loop.Syntax switch
        {
            WhileStatementSyntax s => s.WhileKeyword,
            DoStatementSyntax s => s.DoKeyword,
            ForStatementSyntax s => s.ForKeyword,
            CommonForEachStatementSyntax s => s.ForEachKeyword,
            _ => default,
        };
        var location = keyword == default ? loop.Syntax.GetLocation() : keyword.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, keyword == default ? "loop" : keyword.ValueText));
    }

    private static bool Overrides(IMethodSymbol? method, IMethodSymbol baseMethod)
    {
        for (; method is not null; method = method.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(method, baseMethod))
                return true;
        }

        return false;
    }

    private static bool HasCatchForException(IOperation body, INamedTypeSymbol exception)
    {
        var statements = body is IBlockOperation block ? block.Operations : ImmutableArray.Create(body);

        foreach (var statement in statements)
        {
            switch (statement)
            {
                // "catch" without a type has System.Object as its exception type.
                case ITryOperation tryStatement when tryStatement.Catches.Any(c =>
                    SymbolEqualityComparer.Default.Equals(c.ExceptionType, exception)
                    || c.ExceptionType.SpecialType == SpecialType.System_Object):
                    return true;

                // A per-pass scope (using block), a lock or a plain inner block wraps the whole pass as well.
                // Conditions, inner loops, lambdas and local functions do not: they cover only part of it.
                case IUsingOperation usingStatement when HasCatchForException(usingStatement.Body, exception):
                case ILockOperation lockStatement when HasCatchForException(lockStatement.Body, exception):
                case IBlockOperation innerBlock when HasCatchForException(innerBlock, exception):
                    return true;
            }
        }

        return false;
    }
}
