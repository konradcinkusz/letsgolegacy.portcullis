using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Portcullis.Rules;

namespace Portcullis.Engine.Tests.Rules.Mutants;

/// <summary>
/// Mutation-pass variant of <see cref="SyncOverAsyncAnalyzer"/>'s invocation check
/// (docs/MUTATIONS.md section 8, variant "sync-over-async-generic-awaiters-dropped"). The
/// awaiter list keeps the four non-generic awaiters and loses the four generic ones —
/// <c>TaskAwaiter`1</c> and its siblings — so <c>task.GetAwaiter().GetResult()</c> on a
/// <c>Task&lt;T&gt;</c>, which is how blocking code actually gets a value out, is never
/// reported. Only the invocation path is reproduced, and without the rule's two
/// "known complete" exclusions: those can only suppress a report, so leaving them out
/// cannot be why the mutant misses. Reuses the real
/// <see cref="SyncOverAsyncAnalyzer.SyncOverAsyncRule"/> descriptor.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncOverAsyncGenericAwaitersDroppedMutant : DiagnosticAnalyzer
{
    // The mutation: the four `…Awaiter`1` / `…Awaitable`1+…` entries are missing.
    private static readonly ImmutableHashSet<string> AwaiterTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.Runtime.CompilerServices.TaskAwaiter",
        "System.Runtime.CompilerServices.ValueTaskAwaiter",
        "System.Runtime.CompilerServices.ConfiguredTaskAwaitable+ConfiguredTaskAwaiter",
        "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable+ConfiguredValueTaskAwaiter");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(SyncOverAsyncAnalyzer.SyncOverAsyncRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var owner = MigrationSymbols.MetadataName(method.ContainingType);
        var blocks = (owner == "System.Threading.Tasks.Task" && method.Name is "Wait" or "WaitAll" or "WaitAny")
            || (method.Name == "GetResult" && AwaiterTypes.Contains(owner));

        if (blocks)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                SyncOverAsyncAnalyzer.SyncOverAsyncRule,
                invocation.Syntax.GetLocation(),
                invocation.Syntax.ToString(),
                $"{method.ContainingType.Name}.{method.Name}()"));
        }
    }
}
