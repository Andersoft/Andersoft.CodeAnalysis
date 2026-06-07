using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Linq;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap039Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP039",
        "Data clump — the same parameter group recurs across methods",
        "Method '{0}' shares the parameter group ({1}) with {2} other methods — a data clump; introduce a parameter object (record) so the group travels together",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP039");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    /// <summary>Minimum number of parameters that constitutes a clump.</summary>
    private const int MinDataClumpSize = 3;

    /// <summary>Minimum number of methods the clump must recur in.</summary>
    private const int MinDataClumpOccurrences = 3;

    private static void CollectParameterClumps(
        SymbolAnalysisContext context,
        ConcurrentDictionary<string, ConcurrentBag<(IMethodSymbol Method, Location Location)>> clumps)
    {
        if (context.Symbol is not IMethodSymbol method)
        {
            return;
        }

        // Concrete, original declarations only: overrides and interface
        // declarations restate a signature that is already counted elsewhere.
        if (method.MethodKind != MethodKind.Ordinary ||
            method.IsOverride ||
            method.IsAbstract ||
            method.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return;
        }

        if (method.Locations.IsDefaultOrEmpty || method.Locations[0].SourceTree is null ||
            IsGeneratedFile(method.Locations[0].SourceTree!.FilePath))
        {
            return;
        }

        var descriptors = method.Parameters
            .Where(parameter => parameter.Type.ToDisplayString() != "System.Threading.CancellationToken")
            .Select(parameter => $"{parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {parameter.Name}")
            .OrderBy(descriptor => descriptor, StringComparer.Ordinal)
            .ToArray();

        if (descriptors.Length < MinDataClumpSize)
        {
            return;
        }

        // Record every 3-element combination so partially overlapping clumps
        // still match across methods.
        for (var i = 0; i < descriptors.Length - 2; i++)
        {
            for (var j = i + 1; j < descriptors.Length - 1; j++)
            {
                for (var k = j + 1; k < descriptors.Length; k++)
                {
                    var key = $"{descriptors[i]}, {descriptors[j]}, {descriptors[k]}";
                    clumps.GetOrAdd(key, _ => new ConcurrentBag<(IMethodSymbol, Location)>())
                        .Add((method, method.Locations[0]));
                }
            }
        }
    }

    private static void AnalyzeRap039DataClumps(
        CompilationAnalysisContext context,
        ConcurrentDictionary<string, ConcurrentBag<(IMethodSymbol Method, Location Location)>> clumps)
    {
        var reportedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        // Widest clumps first so each method is reported for its strongest group.
        foreach (var entry in clumps.OrderByDescending(e => e.Value.Count).ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            var distinct = new Dictionary<IMethodSymbol, Location>(SymbolEqualityComparer.Default);
            foreach (var (method, location) in entry.Value)
            {
                distinct[method] = location;
            }

            if (distinct.Count < MinDataClumpOccurrences)
            {
                continue;
            }

            foreach (var pair in distinct)
            {
                if (!reportedMethods.Add(pair.Key))
                {
                    continue;
                }

                Report(context, Diagnostic.Create(
                    Rap039Analyzer.Rule,
                    pair.Value,
                    pair.Key.Name,
                    entry.Key,
                    distinct.Count - 1));
            }
        }
    }
}
