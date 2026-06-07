using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Linq;

namespace Andersoft.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap022Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP022",
        "Feature flag metadata missing owner/expiry",
        "Feature flag metadata missing owner/expiry",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        ArchitectureConventionsAnalyzer.InitializeRule(context, "RAP022");
    }
}

internal static partial class ArchitectureConventionsAnalyzer
{
    private static void AnalyzeRap022FeatureFlagMetadata(SymbolAnalysisContext context, INamedTypeSymbol namedType)
    {
        var fields = namedType.GetMembers().OfType<IFieldSymbol>().Where(static f => f.IsConst && f.Type.SpecialType == SpecialType.System_String).ToArray();
        if (fields.Length == 0)
        {
            return;
        }

        foreach (var field in fields)
        {
            if (!field.Name.StartsWith("FeatureFlag", StringComparison.Ordinal))
            {
                continue;
            }

            var baseName = field.Name;
            var ownerName = $"{baseName}Owner";
            var expiryName = $"{baseName}Expiry";

            var hasOwner = fields.Any(f => f.Name.Equals(ownerName, StringComparison.Ordinal));
            var hasExpiry = fields.Any(f => f.Name.Equals(expiryName, StringComparison.Ordinal));
            if (hasOwner && hasExpiry)
            {
                continue;
            }

            Report(context, Diagnostic.Create(
                Rap022Analyzer.Rule,
                field.Locations[0],
                baseName,
                ownerName,
                expiryName));
        }
    }
}
