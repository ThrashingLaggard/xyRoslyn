using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace xyRoslyn.Analyzers.EfCore;

/// <summary>
/// Reports Where, OrderBy, OrderByDescending, Count, LongCount and Any, and the Count or Length
/// property, applied directly to the result of ToList, ToArray or AsEnumerable on an IQueryable,
/// or to an awaited ToListAsync or ToArrayAsync (ef-core EF-002). A materialized result stored in a
/// variable first is not followed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MaterializedBeforeQueryAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> QueryOperators =
        ImmutableHashSet.Create("Where", "OrderBy", "OrderByDescending", "Count", "LongCount", "Any");

    private static readonly ImmutableHashSet<string> Materializers = ImmutableHashSet.Create("ToList", "ToArray", "AsEnumerable");

    private static readonly ImmutableHashSet<string> AsyncMaterializers = ImmutableHashSet.Create("ToListAsync", "ToArrayAsync");

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.MaterializedBeforeQuery,
        title: "Filter, sort and count in the database",
        messageFormat: "'{0}' loads the whole query into memory before '{1}'; apply '{1}' to the query instead",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Materializing a query before Where, OrderBy, Count or Any loads every row and does the work in memory " +
                     "(ef-core EF-002). The operator belongs on the IQueryable, so the database filters, sorts and counts.",
        helpLinkUri: HelpLinks.Base + DiagnosticIds.MaterializedBeforeQuery + ".md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? queryable = start.Compilation.GetTypeByMetadataName("System.Linq.IQueryable");
            INamedTypeSymbol? enumerable = start.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
            if (queryable is null || enumerable is null)
                return;

            INamedTypeSymbol? efExtensions = start.Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
            KnownTypes types = new KnownTypes(queryable, enumerable, efExtensions);

            start.RegisterOperationAction(c => AnalyzeInvocation(c, types), OperationKind.Invocation);
            start.RegisterOperationAction(c => AnalyzeProperty(c, types), OperationKind.PropertyReference);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownTypes types)
    {
        if (context.Operation is not IInvocationOperation { Arguments.Length: > 0 } invocation
            || !QueryOperators.Contains(invocation.TargetMethod.Name)
            || !SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, types.Enumerable))
        {
            return;
        }

        IOperation source = AnalyzerHelpers.WithoutImplicitConversions(invocation.Arguments[0].Value);
        if (MaterializerName(source, types) is { } materializer)
            context.ReportDiagnostic(Diagnostic.Create(Rule, AnalyzerHelpers.MethodName(invocation), materializer, invocation.TargetMethod.Name));
    }

    private static void AnalyzeProperty(OperationAnalysisContext context, KnownTypes types)
    {
        if (context.Operation is not IPropertyReferenceOperation { Property.Name: "Count" or "Length", Instance: { } instance } property)
            return;

        if (MaterializerName(AnalyzerHelpers.WithoutImplicitConversions(instance), types) is { } materializer)
            context.ReportDiagnostic(Diagnostic.Create(Rule, AnalyzerHelpers.MemberName(property), materializer, property.Property.Name));
    }

    private static string? MaterializerName(IOperation source, KnownTypes types)
    {
        if (source is IAwaitOperation awaited)
        {
            return AnalyzerHelpers.WithoutImplicitConversions(awaited.Operation) is IInvocationOperation { Arguments.Length: > 0 } asyncCall
                   && types.EfExtensions is not null
                   && AsyncMaterializers.Contains(asyncCall.TargetMethod.Name)
                   && SymbolEqualityComparer.Default.Equals(asyncCall.TargetMethod.ContainingType, types.EfExtensions)
                   && IsQueryable(asyncCall.Arguments[0].Value, types)
                ? asyncCall.TargetMethod.Name
                : null;
        }

        return source is IInvocationOperation { Arguments.Length: > 0 } call
               && Materializers.Contains(call.TargetMethod.Name)
               && SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, types.Enumerable)
               && IsQueryable(call.Arguments[0].Value, types)
            ? call.TargetMethod.Name
            : null;
    }

    private static bool IsQueryable(IOperation argument, KnownTypes types)
        => AnalyzerHelpers.IsOrImplements(AnalyzerHelpers.WithoutImplicitConversions(argument).Type, types.Queryable);

    private sealed class KnownTypes(INamedTypeSymbol queryable, INamedTypeSymbol enumerable, INamedTypeSymbol? efExtensions)
    {
        public INamedTypeSymbol Queryable { get; } = queryable;

        public INamedTypeSymbol Enumerable { get; } = enumerable;

        public INamedTypeSymbol? EfExtensions { get; } = efExtensions;
    }
}
