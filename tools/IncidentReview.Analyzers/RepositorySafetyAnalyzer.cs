using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace IncidentReview.Analyzers;

/// <summary>
/// Enforces repository safety rules that require semantic source analysis.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RepositorySafetyAnalyzer : DiagnosticAnalyzer
{
    public const string UnsafeSqlConstructionId = "IR0001";
    public const string MissingDapperTransactionId = "IR0002";
    public const string NestedServiceProviderId = "IR0003";

    private const string DapperNamespace = "Dapper";
    private const string DapperType = "SqlMapper";
    private const string HostAssembly = "IncidentReview.Host.Wpf";
    private const string DependencyInjectionNamespace = "Microsoft.Extensions.DependencyInjection";
    private const string ServiceProviderExtensionsType = "ServiceCollectionContainerBuilderExtensions";

    private static readonly DiagnosticDescriptor UnsafeSqlConstructionRule = new(
        UnsafeSqlConstructionId,
        "Parameterize runtime SQL values",
        "Dapper SQL must not be built with runtime string interpolation or concatenation",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Pass runtime SQL values through Dapper parameters. SQL text passed to Dapper must not be assembled with interpolation or non-constant string concatenation.");

    private static readonly DiagnosticDescriptor MissingDapperTransactionRule = new(
        MissingDapperTransactionId,
        "Supply the active transaction",
        "Dapper mutation '{0}' must receive an explicit non-null transaction",
        "Reliability",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every Dapper Execute-family call in IncidentReview.Store.Sqlite must explicitly receive the transaction owned by the store command executor.");

    private static readonly DiagnosticDescriptor NestedServiceProviderRule = new(
        NestedServiceProviderId,
        "Do not build nested service providers",
        "IServiceCollection.BuildServiceProvider is allowed only in the Host composition root",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Library registration code must add services to the supplied IServiceCollection and must not create a nested service provider.");

    private static readonly ImmutableArray<DiagnosticDescriptor> Rules = ImmutableArray.Create(
        UnsafeSqlConstructionRule,
        MissingDapperTransactionRule,
        NestedServiceProviderRule);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Rules;

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;

        if (IsDapperMethod(method))
        {
            AnalyzeDapperSql(context, invocation);
            AnalyzeDapperTransaction(context, invocation, method);
        }

        if (IsBuildServiceProvider(method)
            && !string.Equals(context.Compilation.AssemblyName, HostAssembly, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(NestedServiceProviderRule, invocation.Syntax.GetLocation()));
        }
    }

    private static void AnalyzeDapperSql(
        OperationAnalysisContext context,
        IInvocationOperation invocation)
    {
        var sqlArgument = FindArgument(invocation, "sql", requireExplicit: false);
        if (sqlArgument is null || !ContainsUnsafeSqlConstruction(sqlArgument.Value))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(UnsafeSqlConstructionRule, sqlArgument.Syntax.GetLocation()));
    }

    private static void AnalyzeDapperTransaction(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        IMethodSymbol method)
    {
        if (!IsMutationMethod(method)
            || !string.Equals(context.Compilation.AssemblyName, "IncidentReview.Store.Sqlite", StringComparison.Ordinal))
        {
            return;
        }

        var transactionArgument = FindArgument(invocation, "transaction", requireExplicit: true);
        if (transactionArgument is not null && !IsDefinitelyNull(transactionArgument.Value))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            MissingDapperTransactionRule,
            invocation.Syntax.GetLocation(),
            method.Name));
    }

    private static IArgumentOperation? FindArgument(
        IInvocationOperation invocation,
        string parameterName,
        bool requireExplicit)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (string.Equals(argument.Parameter?.Name, parameterName, StringComparison.Ordinal)
                && (!requireExplicit || !argument.IsImplicit))
            {
                return argument;
            }
        }

        return null;
    }

    private static bool ContainsUnsafeSqlConstruction(IOperation operation)
    {
        operation = Unwrap(operation);

        if (operation is IInterpolatedStringOperation)
        {
            return true;
        }

        return operation is IBinaryOperation binary
            && binary.OperatorKind == BinaryOperatorKind.Add
            && binary.Type?.SpecialType == SpecialType.System_String
            && !binary.ConstantValue.HasValue;
    }

    private static bool IsDefinitelyNull(IOperation operation)
    {
        operation = Unwrap(operation);
        return operation is IDefaultValueOperation
            || (operation.ConstantValue.HasValue && operation.ConstantValue.Value is null);
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool IsDapperMethod(IMethodSymbol method)
    {
        return string.Equals(method.ContainingType.Name, DapperType, StringComparison.Ordinal)
            && string.Equals(method.ContainingNamespace.ToDisplayString(), DapperNamespace, StringComparison.Ordinal)
            && (method.Name.StartsWith("Query", StringComparison.Ordinal)
                || method.Name.StartsWith("Execute", StringComparison.Ordinal));
    }

    private static bool IsMutationMethod(IMethodSymbol method)
    {
        return method.Name.StartsWith("Execute", StringComparison.Ordinal);
    }

    private static bool IsBuildServiceProvider(IMethodSymbol method)
    {
        return string.Equals(method.Name, "BuildServiceProvider", StringComparison.Ordinal)
            && string.Equals(method.ContainingType.Name, ServiceProviderExtensionsType, StringComparison.Ordinal)
            && string.Equals(
                method.ContainingNamespace.ToDisplayString(),
                DependencyInjectionNamespace,
                StringComparison.Ordinal)
            && method.IsExtensionMethod
            && method.Parameters.Length > 0
            && string.Equals(
                method.Parameters[0].Type.ToDisplayString(),
                "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                StringComparison.Ordinal);
    }
}
