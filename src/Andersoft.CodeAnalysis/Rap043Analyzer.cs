using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap043Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP043",
        "Repeated switch — the same enum is branched on in many places",
        "Switch on enum '{0}' recurs across {1} different types — the same decision is scattered through the codebase; replace the branching with polymorphism or a single strategy/map",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP043");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    /// <summary>
    /// Number of distinct types switching on the same enum before the
    /// scattered branching counts as a smell.
    /// </summary>
    private const int MinRepeatedSwitchTypeCount = 3;

    private static void CollectEnumSwitchSites(
        SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<(INamedTypeSymbol? Container, Location Location)>> sites)
    {
        if (IsGeneratedFile(context.Node.SyntaxTree.FilePath))
        {
            return;
        }

        var (expression, location) = context.Node switch
        {
            SwitchStatementSyntax switchStatement => (switchStatement.Expression, switchStatement.SwitchKeyword.GetLocation()),
            SwitchExpressionSyntax switchExpression => ((ExpressionSyntax?)switchExpression.GoverningExpression, switchExpression.SwitchKeyword.GetLocation()),
            _ => (null, null),
        };

        if (expression is null || location is null)
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(expression).Type;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        // Only enums owned by this codebase — those are the ones whose
        // branching could be replaced with polymorphism.
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType ||
            !SymbolEqualityComparer.Default.Equals(enumType.ContainingAssembly, context.SemanticModel.Compilation.Assembly))
        {
            return;
        }

        sites.GetOrAdd(enumType, _ => new ConcurrentBag<(INamedTypeSymbol?, Location)>())
            .Add((context.ContainingSymbol?.ContainingType, location));
    }

    private static void AnalyzeRap043RepeatedSwitches(
        CompilationAnalysisContext context,
        ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<(INamedTypeSymbol? Container, Location Location)>> sites)
    {
        foreach (var entry in sites)
        {
            var containers = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var (container, _) in entry.Value)
            {
                if (container is not null)
                {
                    containers.Add(container);
                }
            }

            if (containers.Count < MinRepeatedSwitchTypeCount)
            {
                continue;
            }

            foreach (var (_, location) in entry.Value)
            {
                Report(context, Diagnostic.Create(
                    Rap043Analyzer.Rule,
                    location,
                    entry.Key.Name,
                    containers.Count));
            }
        }
    }
}
