using System.Threading.Tasks;

namespace AnalyzerConsumer;

/// <summary>
/// Blocks on a task on purpose: PORTCULLIS_MIG_SYNC_OVER_ASYNC reported on this line is the
/// proof that the compiler loaded and ran the analyzers, not merely that it did not refuse them.
/// </summary>
public static class PendingOrders
{
    public static string Now(Task<string> pending) => pending.Result;
}
