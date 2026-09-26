using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Portcullis.Rules;

/// <summary>
/// Migration rule <c>PORTCULLIS_MIG_SYNC_OVER_ASYNC</c>: blocking a thread on a task —
/// <c>task.Result</c>, <c>task.Wait(…)</c>, <c>Task.WaitAll</c>/<c>Task.WaitAny</c>, and
/// <c>task.GetAwaiter().GetResult()</c> (with or without <c>ConfigureAwait</c>) on
/// <c>Task</c>, <c>Task&lt;T&gt;</c>, <c>ValueTask</c> and <c>ValueTask&lt;T&gt;</c>.
///
/// It is the idiom a mechanical migration produces most: a synchronous .NET Framework call
/// site meets an async-only modern API and gets <c>.Result</c> glued on. Under classic
/// ASP.NET's synchronization context that deadlocks; on ASP.NET Core it does not deadlock
/// but parks a thread-pool thread per request, which is how a service that passed every
/// test falls over at its first real load. docs/rules/MIGRATION.md has the full reasoning.
///
/// Semantic throughout: a member is recognised by the metadata name of the type that
/// declares it (see <see cref="MigrationSymbols.MetadataName"/>), so a <c>Result</c>
/// property or a <c>Wait()</c> method on anything that is not a task — a team's own
/// <c>Outcome.Result</c>, <c>SemaphoreSlim.Wait()</c>, <c>Monitor.Wait</c> — is never
/// reported, and <c>nameof(Task&lt;int&gt;.Result)</c> is not a read.
///
/// Two shapes are recognised as not blocking, because the task is known to have finished
/// at that point and the rule can prove it from the code in front of it:
/// <list type="bullet">
/// <item>the antecedent inside a <c>ContinueWith</c> continuation
/// (<c>t.ContinueWith(prev =&gt; prev.Result)</c>);</item>
/// <item>the same local, parameter, field or property on the true branch of a condition
/// that requires <c>IsCompleted</c> or <c>IsCompletedSuccessfully</c> — the classic
/// synchronous fast path (<c>if (t.IsCompletedSuccessfully) return t.Result;</c>).</item>
/// </list>
/// Everything else that is known complete only through control flow — <c>.Result</c> after
/// <c>await Task.WhenAll(…)</c>, after an early return on <c>!IsCompleted</c> — is still
/// reported; the fix there is to <c>await</c> the finished task, which costs nothing.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncOverAsyncAnalyzer : DiagnosticAnalyzer
{
    private const string Id = "PORTCULLIS_MIG_SYNC_OVER_ASYNC";
    private const string Title = "Blocking wait on a task (sync over async)";
    private const string MessageFormat =
        "'{0}' blocks the calling thread until the task completes ({1}). Under a " +
        "synchronization context that deadlocks; on ASP.NET Core it holds a thread-pool " +
        "thread for the whole wait. Make the caller async and await the task instead.";

    private const string TaskType = "System.Threading.Tasks.Task";
    private const string GenericTaskType = "System.Threading.Tasks.Task`1";
    private const string ValueTaskType = "System.Threading.Tasks.ValueTask";
    private const string GenericValueTaskType = "System.Threading.Tasks.ValueTask`1";

    private static readonly ImmutableHashSet<string> TaskTypes =
        ImmutableHashSet.Create(StringComparer.Ordinal, TaskType, GenericTaskType, ValueTaskType, GenericValueTaskType);

    // What `task.GetAwaiter()` and `task.ConfigureAwait(…).GetAwaiter()` return, for each of
    // the four task types. `GetResult()` on any of them blocks until the task completes.
    private static readonly ImmutableHashSet<string> AwaiterTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.Runtime.CompilerServices.TaskAwaiter",
        "System.Runtime.CompilerServices.TaskAwaiter`1",
        "System.Runtime.CompilerServices.ValueTaskAwaiter",
        "System.Runtime.CompilerServices.ValueTaskAwaiter`1",
        "System.Runtime.CompilerServices.ConfiguredTaskAwaitable+ConfiguredTaskAwaiter",
        "System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter",
        "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable+ConfiguredValueTaskAwaiter",
        "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1+ConfiguredValueTaskAwaiter");

    private static readonly SymbolDisplayFormat MemberOwnerFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static readonly DiagnosticDescriptor SyncOverAsyncRule = new(
        Id, Title, MessageFormat, MigrationSymbols.Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(SyncOverAsyncRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var isExempt = MigrationSymbols.ExemptionFor(PortcullisConventions.From(start.Options));
            start.RegisterOperationAction(c => AnalyzeResult(c, isExempt), OperationKind.PropertyReference);
            start.RegisterOperationAction(c => AnalyzeInvocation(c, isExempt), OperationKind.Invocation);
        });
    }

    // task.Result
    private static void AnalyzeResult(OperationAnalysisContext context, Func<SyntaxTree, bool> isExempt)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        var property = reference.Property;
        if (!property.IsStatic
            && property.Name == "Result"
            && MigrationSymbols.MetadataName(property.ContainingType) is GenericTaskType or GenericValueTaskType)
        {
            ReportUnlessKnownComplete(context, reference, reference.Instance, property, isExempt);
        }
    }

    // task.Wait(…), Task.WaitAll(…), Task.WaitAny(…), awaiter.GetResult()
    private static void AnalyzeInvocation(OperationAnalysisContext context, Func<SyntaxTree, bool> isExempt)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var owner = MigrationSymbols.MetadataName(method.ContainingType);

        // Static WaitAll/WaitAny have no instance, so there is no single task to prove complete.
        if (owner == TaskType && method.Name is "Wait" or "WaitAll" or "WaitAny")
        {
            ReportUnlessKnownComplete(context, invocation, invocation.Instance, method, isExempt);
        }
        else if (method.Name == "GetResult" && AwaiterTypes.Contains(owner))
        {
            ReportUnlessKnownComplete(context, invocation, TaskBehindAwaiter(invocation.Instance), method, isExempt);
        }
    }

    private static void ReportUnlessKnownComplete(
        OperationAnalysisContext context, IOperation blocking, IOperation? task, ISymbol member, Func<SyntaxTree, bool> isExempt)
    {
        if (MigrationSymbols.IsInsideNameOf(blocking)
            || isExempt(blocking.Syntax.SyntaxTree)
            || IsContinuationAntecedent(task, blocking)
            || IsGuardedByCompletionCheck(task, blocking))
        {
            return;
        }

        var owner = member.ContainingType.OriginalDefinition.ToDisplayString(MemberOwnerFormat);
        var parentheses = member.Kind == SymbolKind.Method ? "()" : string.Empty;
        context.ReportDiagnostic(Diagnostic.Create(
            SyncOverAsyncRule, blocking.Syntax.GetLocation(), blocking.Syntax.ToString(), $"{owner}.{member.Name}{parentheses}"));
    }

    // `task.GetAwaiter()` or `task.ConfigureAwait(false).GetAwaiter()` → `task`. An awaiter
    // held in a variable has no task the rule can see, so it answers null.
    private static IOperation? TaskBehindAwaiter(IOperation? awaiter)
    {
        var getAwaiter = awaiter as IInvocationOperation;
        if (getAwaiter == null || getAwaiter.TargetMethod.Name != "GetAwaiter")
        {
            return null;
        }

        var configureAwait = getAwaiter.Instance as IInvocationOperation;
        return configureAwait != null && configureAwait.TargetMethod.Name == "ConfigureAwait"
            ? configureAwait.Instance
            : getAwaiter.Instance;
    }

    /// <summary>
    /// <c>t.ContinueWith(prev =&gt; prev.Result)</c>: the antecedent task, which has always
    /// completed by the time its continuation runs. The antecedent is the only task-typed
    /// parameter a <c>ContinueWith</c> continuation has (the other one is the untyped
    /// state), so a lambda parameter read as a task inside such a continuation is it.
    /// </summary>
    private static bool IsContinuationAntecedent(IOperation? task, IOperation blocking)
    {
        var lambda = (task as IParameterReferenceOperation)?.Parameter.ContainingSymbol;
        if (lambda == null)
        {
            return false;
        }

        for (var current = blocking.Parent; current != null; current = current.Parent)
        {
            var function = current as IAnonymousFunctionOperation;
            if (function != null && SymbolEqualityComparer.Default.Equals(function.Symbol, lambda))
            {
                return IsArgumentToATaskMethod(function);
            }
        }

        return false;
    }

    // lambda → delegate creation → argument → a method of Task/Task<T>/ValueTask/ValueTask<T>.
    // ContinueWith is the only such method whose delegate receives a task, so this is the
    // continuation shape without naming it — a name check here could never be observed.
    private static bool IsArgumentToATaskMethod(IAnonymousFunctionOperation function)
    {
        var invocation = function.Parent?.Parent?.Parent as IInvocationOperation;
        return invocation != null && TaskTypes.Contains(MigrationSymbols.MetadataName(invocation.TargetMethod.ContainingType));
    }

    /// <summary>
    /// <c>if (t.IsCompleted) … t.Result …</c> and <c>t.IsCompletedSuccessfully ? t.Result : …</c>:
    /// the blocking access sits on the true branch of a condition that, as a conjunction,
    /// requires the same task to be complete. A negated or or-ed check proves nothing and is
    /// not accepted.
    /// </summary>
    private static bool IsGuardedByCompletionCheck(IOperation? task, IOperation blocking)
    {
        var taskSymbol = ReferencedSymbol(task);
        if (taskSymbol == null)
        {
            return false;
        }

        for (var current = blocking.Parent; current != null; current = current.Parent)
        {
            var conditional = current as IConditionalOperation;
            if (conditional != null
                && conditional.WhenTrue.Syntax.Span.Contains(blocking.Syntax.Span)
                && RequiresCompletion(conditional.Condition, taskSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequiresCompletion(IOperation condition, ISymbol taskSymbol) =>
        condition.DescendantsAndSelf().Any(operation => IsCompletionCheckOf(operation, taskSymbol) && IsConjunct(operation, condition));

    private static bool IsCompletionCheckOf(IOperation operation, ISymbol taskSymbol)
    {
        var check = operation as IPropertyReferenceOperation;
        return check != null
            && check.Property.Name is "IsCompleted" or "IsCompletedSuccessfully"
            && TaskTypes.Contains(MigrationSymbols.MetadataName(check.Property.ContainingType))
            && SymbolEqualityComparer.Default.Equals(ReferencedSymbol(check.Instance), taskSymbol);
    }

    // Every step from the check up to the condition's root is an `&&`, so the whole
    // condition cannot be true unless the check is.
    private static bool IsConjunct(IOperation check, IOperation condition)
    {
        for (var current = check; current != condition; current = current.Parent!)
        {
            var and = current.Parent as IBinaryOperation;
            if (and == null || and.OperatorKind != BinaryOperatorKind.ConditionalAnd)
            {
                return false;
            }
        }

        return true;
    }

    // The variable a task expression reads: a local, a parameter, or a field or property of
    // this instance (or a static one). Anything else — a call, an element access, a member of
    // another object — cannot be shown to be the same task twice.
    private static ISymbol? ReferencedSymbol(IOperation? operation) => operation switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        IFieldReferenceOperation { Instance: null or IInstanceReferenceOperation } field => field.Field,
        IPropertyReferenceOperation { Instance: null or IInstanceReferenceOperation, Arguments.IsEmpty: true } property
            => property.Property,
        _ => null,
    };
}
