using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using xyRoslyn.Analyzers;

namespace xyRoslyn.CodeFixes.Http;

/// <summary>
/// Wraps the reported interpolation hole in Uri.EscapeDataString. Offered only for strings that are
/// known not to be null (EscapeDataString throws on null) and for holes without alignment or format.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnescapedUrlPathValueCodeFix)), Shared]
public sealed class UnescapedUrlPathValueCodeFix : CodeFixProvider
{
    private const string Title = "Escape with Uri.EscapeDataString";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.UnescapedUrlPathValue);

    // Each fix only changes its own interpolation hole, so the fixes never overlap.
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true) is not ExpressionSyntax expression
            || expression.Parent is not InterpolationSyntax { AlignmentClause: null, FormatClause: null })
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var typeInfo = model?.GetTypeInfo(expression, context.CancellationToken);
        if (typeInfo is not { Type.SpecialType: SpecialType.System_String, Nullability.FlowState: NullableFlowState.NotNull })
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => Task.FromResult(Wrap(context.Document, root, expression, UriTypeName(model!, expression), ct)),
                equivalenceKey: Title),
            context.Diagnostics);
    }

    // The shortest name that binds to System.Uri at this position: "Uri" when System is imported
    // and nothing else named Uri is in scope, otherwise "System.Uri", otherwise fully qualified.
    // Chosen here instead of by the simplifier, which also rewrites the spacing inside the hole.
    private static string UriTypeName(SemanticModel model, ExpressionSyntax expression)
    {
        var uri = model.Compilation.GetTypeByMetadataName("System.Uri");
        var position = expression.SpanStart;

        var shortName = model.LookupNamespacesAndTypes(position, name: "Uri");
        if (shortName.Length == 1 && SymbolEqualityComparer.Default.Equals(shortName[0], uri))
            return "Uri";

        var system = model.LookupNamespacesAndTypes(position, name: "System");
        if (system.Length == 1 && system[0] is INamespaceSymbol { ContainingNamespace.IsGlobalNamespace: true })
            return "System.Uri";

        return "global::System.Uri";
    }

    private static Document Wrap(
        Document document, SyntaxNode root, ExpressionSyntax expression, string uriTypeName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var escaped = SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression(uriTypeName + ".EscapeDataString"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(expression.WithoutTrivia()))))
            .WithTriviaFrom(expression);

        return document.WithSyntaxRoot(root.ReplaceNode(expression, escaped));
    }
}
