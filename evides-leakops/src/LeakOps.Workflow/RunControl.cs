using System.Collections.Concurrent;

namespace LeakOps.Workflow;

/// <summary>
/// Keeps the runs that the operator paused. A paused run waits on a TaskCompletionSource (like a Python asyncio.Future) until
/// Resume or Cancel completes it, or until the time limit passes, which counts as a cancel. In memory: one web instance.
/// </summary>
public class RunControl(IConfiguration configuration)
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _paused = new();

    public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(int.TryParse(configuration["PAUSE_TIMEOUT_SECONDS"], out var s) && s > 0 ? s : 900);

    /// <summary>
    /// Registers the run as paused, calls announce, then waits. True = resume; false = cancelled, timed out, or the operator page went away
    /// (<paramref name="clientGone"/>): nothing has been written at that point, so letting the run end is safe.
    /// </summary>
    public async Task<bool> WaitAsync(string runId, Func<ValueTask> announce, CancellationToken cancellationToken, CancellationToken clientGone = default)
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _paused[runId] = gate;
        try
        {
            await announce();
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, clientGone);
            stop.CancelAfter(Timeout);
            try { return await gate.Task.WaitAsync(stop.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }   // time limit or page closed
        }
        finally
        {
            _paused.TryRemove(runId, out _);
        }
    }

    public bool Resume(string runId) => _paused.TryGetValue(runId, out var gate) && gate.TrySetResult(true);

    public bool Cancel(string runId) => _paused.TryGetValue(runId, out var gate) && gate.TrySetResult(false);
}
