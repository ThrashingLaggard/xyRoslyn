using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace xyRoslyn.Analyzers.EfCore;

/// <summary>
/// Reports Queryable.Skip and Queryable.Take on a query that starts at a DbSet (property, field or
/// DbContext.Set) and has no OrderBy, OrderByDescending, ThenBy, ThenByDescending, Order or
/// OrderDescending in its chain (ef-core EF-006). The chain is followed only through Queryable and
/// EntityFrameworkQueryableExtensions methods; a query that comes from a variable or from another
/// method is not reported, because its ordering is unknown here.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PagingWithoutOrderByAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> Ordering =
        ImmutableHashSet.Create("OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Order", "OrderDescending");

    private static readonly ImmutableHashSet<string> RowLimits = ImmutableHashSet.Create("Skip", "Take");

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.PagingWithoutOrderBy,
        title: "Page only after a unique sort order",
        messageFormat: "'{0}' is applied to a query without OrderBy; the database may return the rows in any order",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Skip and Take without OrderBy return different pages depending on the database (ef-core EF-006). " +
                     "Sort first, and end with ThenBy(x => x.Id) when the sort key is not unique.",
        helpLinkUri: HelpLinks.Base + DiagnosticIds.PagingWithoutOrderBy + ".md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? dbSet = start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1");
            INamedTypeSymbol? dbContext = start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext");
            INamedTypeSymbol? queryable = start.Compilation.GetTypeByMetadataName("System.Linq.Queryable");
            if (dbSet is null || dbContext is null || queryable is null)
                return;

            INamedTypeSymbol? efExtensions = start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
            KnownTypes types = new KnownTypes(dbSet, dbContext, queryable, efExtensions);

            start.RegisterOperationAction(c => AnalyzeInvocation(c, types), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownTypes types)
    {
        if (context.Operation is not IInvocationOperation { Arguments.Length: > 0 } invocation
            || !RowLimits.Contains(invocation.TargetMethod.Name)
            || !SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, types.Queryable))
        {
            return;
        }

        IOperation current = AnalyzerHelpers.WithoutImplicitConversions(invocation.Arguments[0].Value);
        while (current is IInvocationOperation { Arguments.Length: > 0 } call && types.IsChainMethod(call.TargetMethod.ContainingType))
        {
            bool isQueryable = SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, types.Queryable);

            // Ordered: fine. Another Skip or Take further in: that one reports, so the chain reports once.
            if (isQueryable && (Ordering.Contains(call.TargetMethod.Name) || RowLimits.Contains(call.TargetMethod.Name)))
                return;

            current = AnalyzerHelpers.WithoutImplicitConversions(call.Arguments[0].Value);
        }

        if (types.IsDbSetSource(current))
            context.ReportDiagnostic(Diagnostic.Create(Rule, AnalyzerHelpers.MethodName(invocation), invocation.TargetMethod.Name));
    }

    private sealed class KnownTypes(INamedTypeSymbol dbSet, INamedTypeSymbol dbContext, INamedTypeSymbol queryable, INamedTypeSymbol? efExtensions)
    {
        public INamedTypeSymbol Queryable { get; } = queryable;

        public bool IsChainMethod(INamedTypeSymbol type)
            => SymbolEqualityComparer.Default.Equals(type, Queryable) || SymbolEqualityComparer.Default.Equals(type, efExtensions);

        public bool IsDbSetSource(IOperation operation)
            => operation switch
            {
                IPropertyReferenceOperation or IFieldReferenceOperation => AnalyzerHelpers.InheritsFromOrEquals(operation.Type, dbSet),
                IInvocationOperation { TargetMethod.Name: "Set" } set => AnalyzerHelpers.InheritsFromOrEquals(set.TargetMethod.ContainingType, dbContext),
                _ => false,
            };
    }
}
