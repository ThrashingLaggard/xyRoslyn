using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace xyRoslyn.Analyzers.Http;

/// <summary>
/// Reports interpolation holes in the path part of a request URI that do not run through
/// Uri.EscapeDataString (http-clients HTTP-017). Checked are the requestUri arguments of
/// HttpClient, HttpClientJsonExtensions and HttpRequestMessage, also when wrapped in new Uri(...).
/// Integers and Guids are not reported, because their text cannot contain '/', '?' or '#'.
/// A hole at the very start of a complete request URI is a base address, not a path segment; in the
/// relative part of new Uri(baseUri, $"...") it is a path segment. Holes after '?' or '#' are not path.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnescapedUrlPathValueAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.UnescapedUrlPathValue,
        title: "Escape variable values in URL paths",
        messageFormat: "'{0}' is inserted into the URL path without Uri.EscapeDataString",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A '/', '?' or '#' in a value changes which URL is requested (http-clients HTTP-017). " +
                     "Every variable path segment runs through Uri.EscapeDataString.",
        helpLinkUri: HelpLinks.Base + DiagnosticIds.UnescapedUrlPathValue + ".md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            var httpClient = start.Compilation.GetTypeByMetadataName("System.Net.Http.HttpClient");
            var uri = start.Compilation.GetTypeByMetadataName("System.Uri");
            if (httpClient is null || uri is null)
                return;

            var types = new KnownTypes(
                httpClient,
                start.Compilation.GetTypeByMetadataName("System.Net.Http.Json.HttpClientJsonExtensions"),
                start.Compilation.GetTypeByMetadataName("System.Net.Http.HttpRequestMessage"),
                uri,
                start.Compilation.GetTypeByMetadataName("System.Guid"));

            start.RegisterOperationAction(c => AnalyzeArguments(c, ((IInvocationOperation)c.Operation).TargetMethod, ((IInvocationOperation)c.Operation).Arguments, types), OperationKind.Invocation);
            start.RegisterOperationAction(c =>
            {
                if (c.Operation is IObjectCreationOperation { Constructor: { } constructor } creation)
                    AnalyzeArguments(c, constructor, creation.Arguments, types);
            }, OperationKind.ObjectCreation);
        });
    }

    private static void AnalyzeArguments(
        OperationAnalysisContext context, IMethodSymbol method, ImmutableArray<IArgumentOperation> arguments, KnownTypes types)
    {
        if (!types.IsRequestTarget(method.ContainingType))
            return;

        foreach (var argument in arguments)
        {
            if (argument.Parameter?.Name != "requestUri")
                continue;

            var value = AnalyzerHelpers.WithoutImplicitConversions(argument.Value);
            var leadingHoleIsBase = true;

            // HttpClient overloads that take a Uri: look into the string argument of new Uri(...).
            // In new Uri(baseUri, $"...") the string is relative to the base, so its first hole is a path segment.
            if (value is IObjectCreationOperation { Constructor: { } uriConstructor } uriCreation
                && SymbolEqualityComparer.Default.Equals(uriConstructor.ContainingType, types.Uri))
            {
                var stringArgument = uriCreation.Arguments.FirstOrDefault(a => a.Parameter?.Type.SpecialType == SpecialType.System_String);
                if (stringArgument is null)
                    continue;

                leadingHoleIsBase = uriCreation.Arguments.All(a => !SymbolEqualityComparer.Default.Equals(a.Parameter?.Type, types.Uri));
                value = AnalyzerHelpers.WithoutImplicitConversions(stringArgument.Value);
            }

            if (value is IInterpolatedStringOperation interpolated)
                AnalyzeInterpolatedString(context, interpolated, leadingHoleIsBase, types);
        }
    }

    private static void AnalyzeInterpolatedString(
        OperationAnalysisContext context, IInterpolatedStringOperation interpolated, bool leadingHoleIsBase, KnownTypes types)
    {
        var isFirstPart = leadingHoleIsBase;
        foreach (var part in interpolated.Parts)
        {
            if (part is IInterpolatedStringTextOperation text)
            {
                // Everything after the first '?' or '#' is query or fragment, not path.
                if (text.Text.ConstantValue.Value is string literal && literal.IndexOfAny(['?', '#']) >= 0)
                    return;
            }
            else if (part is IInterpolationOperation hole && !isFirstPart && !IsSafe(hole.Expression, types))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, hole.Expression.Syntax.GetLocation(), hole.Expression.Syntax.ToString()));
            }

            isFirstPart = false;
        }
    }

    private static bool IsSafe(IOperation expression, KnownTypes types)
    {
        if (expression.ConstantValue.HasValue)
            return true;

        if (expression is IInvocationOperation { TargetMethod.Name: "EscapeDataString" } invocation
            && SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, types.Uri))
        {
            return true;
        }

        var type = expression.Type;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        return type is not null
               && (type.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte
                       or SpecialType.System_Int16 or SpecialType.System_UInt16
                       or SpecialType.System_Int32 or SpecialType.System_UInt32
                       or SpecialType.System_Int64 or SpecialType.System_UInt64
                   || SymbolEqualityComparer.Default.Equals(type, types.Guid));
    }

    private sealed class KnownTypes(
        INamedTypeSymbol httpClient,
        INamedTypeSymbol? jsonExtensions,
        INamedTypeSymbol? requestMessage,
        INamedTypeSymbol uri,
        INamedTypeSymbol? guid)
    {
        public INamedTypeSymbol Uri { get; } = uri;

        public INamedTypeSymbol? Guid { get; } = guid;

        public bool IsRequestTarget(INamedTypeSymbol type)
            => SymbolEqualityComparer.Default.Equals(type, httpClient)
               || SymbolEqualityComparer.Default.Equals(type, jsonExtensions)
               || SymbolEqualityComparer.Default.Equals(type, requestMessage);
    }
}
