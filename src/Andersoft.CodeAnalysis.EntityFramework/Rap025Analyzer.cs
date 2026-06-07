using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Linq;

namespace Andersoft.CodeAnalysis.EntityFramework;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Rap025Analyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor Rule = new(
        "RAP025",
        "Entity configuration missing concurrency token",
        "Entity configuration for '{0}' should configure a concurrency token (IsConcurrencyToken / IsRowVersion) for optimistic concurrency",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        EfAnalyzer.InitializeRule(context, "RAP025");
    }
}

internal static partial class EfAnalyzer
{
    private static void AnalyzeRap025MissingConcurrencyToken(
        SyntaxNodeAnalysisContext context,
        ClassDeclarationSyntax classDeclaration,
        INamedTypeSymbol namedType,
        INamedTypeSymbol? entityConfigurationTypeSymbol)
    {
        if (entityConfigurationTypeSymbol is null)
        {
            return;
        }

        if (!ImplementsGenericInterface(namedType, entityConfigurationTypeSymbol))
        {
            return;
        }

        var entityType = entityConfigurationTypeSymbol is INamedTypeSymbol configType
            ? namedType.AllInterfaces
                .FirstOrDefault(i => SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, entityConfigurationTypeSymbol))
                ?.TypeArguments.FirstOrDefault()
            : null;

        if (entityType is null)
        {
            return;
        }

        var entityNamespace = entityType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!entityNamespace.Contains(".Domain.", StringComparison.Ordinal))
        {
            return;
        }

        var classSource = classDeclaration.ToString();
        if (classSource.Contains("IsConcurrencyToken", StringComparison.Ordinal) ||
            classSource.Contains("IsRowVersion", StringComparison.Ordinal))
        {
            return;
        }

        Report(context, Diagnostic.Create(
            Rap025Analyzer.Rule,
            classDeclaration.Identifier.GetLocation(),
            entityType.Name));
    }

    private static bool ImplementsGenericInterface(INamedTypeSymbol namedType, INamedTypeSymbol? interfaceSymbol)
    {
        if (interfaceSymbol is null)
        {
            return false;
        }

        foreach (var iface in namedType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface.ConstructedFrom, interfaceSymbol))
            {
                return true;
            }
        }

        return false;
    }
}
