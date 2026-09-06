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
    private const string DapperCommandDefinitionType = "CommandDefinition";
    private const string HostAssembly = "IncidentReview.Host.Wpf";
    private const string SqliteStoreAssembly = "IncidentReview.Store.Sqlite";
    private const string DependencyInjectionNamespace = "Microsoft.Extensions.DependencyInjection";
    private const string ServiceProviderExtensionsType = "ServiceCollectionContainerBuilderExtensions";

    private static readonly DiagnosticDescriptor UnsafeSqlConstructionRule = new(
        UnsafeSqlConstructionId,
        "Parameterize runtime SQL values",
        "Dapper SQL must resolve to compile-time text; pass runtime values as parameters",
        "Security",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Pass runtime SQL values through Dapper parameters. SQL text passed to Dapper must resolve to compile-time text, either directly or through a supported private forwarding method.");

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
        context.RegisterOperationBlockAction(AnalyzeCommandDefinitionFactory);
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
        else
        {
            AnalyzeForwardedSql(context, invocation, method);
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
        if (sqlArgument is not null)
        {
            if (!IsCompileTimeSql(sqlArgument.Value, depth: 0))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsafeSqlConstructionRule,
                    sqlArgument.Syntax.GetLocation()));
            }

            return;
        }

        var commandArgument = FindCommandDefinitionArgument(invocation);
        if (commandArgument is null || CommandDefinitionComponentIsSafe(
                commandArgument.Value,
                CommandDefinitionComponent.Sql,
                depth: 0))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            UnsafeSqlConstructionRule,
            commandArgument.Syntax.GetLocation()));
    }

    private static void AnalyzeDapperTransaction(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        IMethodSymbol method)
    {
        if (!IsMutationMethod(method)
            || !string.Equals(context.Compilation.AssemblyName, SqliteStoreAssembly, StringComparison.Ordinal))
        {
            return;
        }

        var transactionArgument = FindArgument(invocation, "transaction", requireExplicit: true);
        if (transactionArgument is not null && !IsDefinitelyNull(transactionArgument.Value))
        {
            return;
        }

        var commandArgument = FindCommandDefinitionArgument(invocation);
        if (commandArgument is not null && CommandDefinitionComponentIsSafe(
                commandArgument.Value,
                CommandDefinitionComponent.Transaction,
                depth: 0))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            MissingDapperTransactionRule,
            invocation.Syntax.GetLocation(),
            method.Name));
    }

    private static void AnalyzeForwardedSql(
        OperationAnalysisContext context,
        IInvocationOperation invocation,
        IMethodSymbol method)
    {
        if (!IsSupportedSqlForwarder(method))
        {
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (!IsSqlParameter(argument.Parameter)
                || IsCompileTimeSql(argument.Value, depth: 0))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                UnsafeSqlConstructionRule,
                argument.Syntax.GetLocation()));
        }
    }

    private static void AnalyzeCommandDefinitionFactory(OperationBlockAnalysisContext context)
    {
        if (context.OwningSymbol is not IMethodSymbol factory
            || !IsSupportedCommandFactory(factory))
        {
            return;
        }

        var foundReturn = false;
        foreach (var operationBlock in context.OperationBlocks)
        {
            AnalyzeCommandFactoryReturns(context, factory, operationBlock, ref foundReturn);
        }

        if (!foundReturn)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsafeSqlConstructionRule,
                context.OperationBlocks[0].Syntax.GetLocation()));
        }
    }

    private static void AnalyzeCommandFactoryReturns(
        OperationBlockAnalysisContext context,
        IMethodSymbol factory,
        IOperation operation,
        ref bool foundReturn)
    {
        if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
        {
            return;
        }

        if (operation is IReturnOperation returnOperation)
        {
            foundReturn = true;
            AnalyzeCommandFactoryReturn(context, factory, returnOperation);
            return;
        }

        foreach (var child in operation.ChildOperations)
        {
            AnalyzeCommandFactoryReturns(context, factory, child, ref foundReturn);
        }
    }

    private static void AnalyzeCommandFactoryReturn(
        OperationBlockAnalysisContext context,
        IMethodSymbol factory,
        IReturnOperation returnOperation)
    {
        var returnedValue = returnOperation.ReturnedValue is null
            ? null
            : Unwrap(returnOperation.ReturnedValue);
        if (returnedValue is not IObjectCreationOperation creation
            || !IsCommandDefinition(creation.Type))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsafeSqlConstructionRule,
                returnOperation.Syntax.GetLocation()));
            return;
        }

        var sql = FindArgument(creation.Arguments, "commandText", "sql");
        if (sql is null || !IsFactorySql(sql.Value, factory))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsafeSqlConstructionRule,
                sql?.Syntax.GetLocation() ?? creation.Syntax.GetLocation()));
        }

        if (!string.Equals(
                context.Compilation.AssemblyName,
                SqliteStoreAssembly,
                StringComparison.Ordinal))
        {
            return;
        }

        var factoryTransaction = FindParameter(factory, "transaction");
        if (factoryTransaction is null)
        {
            return;
        }

        var transaction = FindArgument(creation.Arguments, "transaction");
        if (transaction is null
            || transaction.IsImplicit
            || !IsParameterReference(transaction.Value, factoryTransaction))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MissingDapperTransactionRule,
                creation.Syntax.GetLocation(),
                DapperCommandDefinitionType));
        }
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

    private static IArgumentOperation? FindCommandDefinitionArgument(
        IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (IsCommandDefinition(argument.Parameter?.Type))
            {
                return argument;
            }
        }

        return null;
    }

    private static bool CommandDefinitionComponentIsSafe(
        IOperation operation,
        CommandDefinitionComponent component,
        int depth)
    {
        if (depth > 16)
        {
            return false;
        }

        operation = Unwrap(operation);
        if (operation is IObjectCreationOperation creation && IsCommandDefinition(creation.Type))
        {
            var names = component == CommandDefinitionComponent.Sql
                ? new[] { "commandText", "sql" }
                : new[] { "transaction" };
            var value = FindArgument(creation.Arguments, names);
            return value is not null && ComponentValueIsSafe(
                value.Value,
                component,
                depth + 1);
        }

        if (operation is IInvocationOperation factory
            && IsSupportedCommandFactory(factory.TargetMethod))
        {
            if (component == CommandDefinitionComponent.Sql)
            {
                return factory.TargetMethod.DeclaringSyntaxReferences.Length > 0;
            }

            var value = FindArgument(factory.Arguments, "transaction");
            return value is not null && ComponentValueIsSafe(
                value.Value,
                component,
                depth + 1);
        }

        return false;
    }

    private static bool ComponentValueIsSafe(
        IOperation operation,
        CommandDefinitionComponent component,
        int depth)
    {
        if (depth > 16)
        {
            return false;
        }

        operation = Unwrap(operation);
        return component == CommandDefinitionComponent.Sql
            ? IsCompileTimeSql(operation, depth + 1)
            : !IsDefinitelyNull(operation);
    }

    private static bool IsCompileTimeSql(IOperation operation, int depth)
    {
        if (depth > 16)
        {
            return false;
        }

        operation = Unwrap(operation);
        return operation.ConstantValue.HasValue && operation.ConstantValue.Value is string
            || IsForwardedSqlParameter(operation);
    }

    private static bool IsFactorySql(IOperation operation, IMethodSymbol factory)
    {
        operation = Unwrap(operation);
        return operation.ConstantValue.HasValue && operation.ConstantValue.Value is string
            || operation is IParameterReferenceOperation parameter
            && SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, factory)
            && IsSqlParameter(parameter.Parameter);
    }

    private static bool IsForwardedSqlParameter(IOperation operation)
    {
        return operation is IParameterReferenceOperation parameter
            && IsSqlParameter(parameter.Parameter)
            && parameter.Parameter.ContainingSymbol is IMethodSymbol method
            && IsSupportedSqlForwarder(method);
    }

    private static bool IsSqlParameter(IParameterSymbol? parameter)
    {
        return parameter?.Type.SpecialType == SpecialType.System_String
            && (string.Equals(parameter.Name, "sql", StringComparison.Ordinal)
                || string.Equals(parameter.Name, "commandText", StringComparison.Ordinal));
    }

    private static IParameterSymbol? FindParameter(
        IMethodSymbol method,
        string parameterName)
    {
        foreach (var parameter in method.Parameters)
        {
            if (string.Equals(parameter.Name, parameterName, StringComparison.Ordinal))
            {
                return parameter;
            }
        }

        return null;
    }

    private static bool IsParameterReference(
        IOperation operation,
        IParameterSymbol expectedParameter)
    {
        operation = Unwrap(operation);
        return operation is IParameterReferenceOperation parameter
            && SymbolEqualityComparer.Default.Equals(parameter.Parameter, expectedParameter);
    }

    private static IArgumentOperation? FindArgument(
        ImmutableArray<IArgumentOperation> arguments,
        params string[] parameterNames)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter is null)
            {
                continue;
            }

            foreach (var parameterName in parameterNames)
            {
                if (string.Equals(argument.Parameter.Name, parameterName, StringComparison.Ordinal))
                {
                    return argument;
                }
            }
        }

        return null;
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

    private static bool IsCommandDefinition(ITypeSymbol? type)
    {
        return type is not null
            && string.Equals(type.Name, DapperCommandDefinitionType, StringComparison.Ordinal)
            && string.Equals(type.ContainingNamespace.ToDisplayString(), DapperNamespace, StringComparison.Ordinal);
    }

    private static bool IsSupportedCommandFactory(IMethodSymbol method)
    {
        return method.DeclaredAccessibility == Accessibility.Private
            && method.DeclaringSyntaxReferences.Length > 0
            && IsCommandDefinition(method.ReturnType);
    }

    private static bool IsSupportedSqlForwarder(IMethodSymbol method)
    {
        return method.DeclaredAccessibility == Accessibility.Private
            && method.DeclaringSyntaxReferences.Length > 0
            && (IsCommandDefinition(method.ReturnType)
                || method.Name.StartsWith("Query", StringComparison.Ordinal)
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

    private enum CommandDefinitionComponent
    {
        Sql,
        Transaction,
    }
}
