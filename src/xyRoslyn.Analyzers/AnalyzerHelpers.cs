using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace xyRoslyn.Analyzers;

internal static class AnalyzerHelpers
{
    // Report on the method name ("SaveChangesAsync"), not on the whole call with its arguments.
    public static Location MethodName(IInvocationOperation invocation)
        => invocation.Syntax switch
        {
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } => access.Name.GetLocation(),
            InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } => binding.Name.GetLocation(),
            InvocationExpressionSyntax { Expression: SimpleNameSyntax name } => name.GetLocation(),
            _ => invocation.Syntax.GetLocation(),
        };

    // Report on the member name of a property access ("Count"), not on the whole expression.
    public static Location MemberName(IOperation operation)
        => operation.Syntax switch
        {
            MemberAccessExpressionSyntax access => access.Name.GetLocation(),
            MemberBindingExpressionSyntax binding => binding.Name.GetLocation(),
            _ => operation.Syntax.GetLocation(),
        };

    // Implicit conversions (boxing, interface conversions) sit between an argument and its value.
    public static IOperation WithoutImplicitConversions(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;
        return operation;
    }

    public static bool InheritsFromOrEquals(ITypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, baseType))
                return true;
        }

        return false;
    }

    public static bool IsOrImplements(ITypeSymbol? type, INamedTypeSymbol interfaceType)
        => type is not null
           && (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, interfaceType)
               || type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, interfaceType)));
}
