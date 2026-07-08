namespace SwiftList.Core.Indexer.NetworkDrive;

// Runs work on a dedicated, BelowNormal-priority thread instead of the shared ThreadPool. A large/slow
// background scan can otherwise saturate the ThreadPool (many workers doing blocking sync I/O) and starve
// unrelated Task.Run-based work -- including the app's own interactive search/launch code -- from getting
// a worker thread promptly. BelowNormal priority additionally lets the OS scheduler favor foreground UI
// work whenever there's real CPU contention. Neither change reduces throughput when the system isn't
// contended, which is true for nearly all of a scan's runtime: idle cores run BelowNormal threads exactly
// as fast as Normal ones. Wrapped back into a Task so callers keep normal Task.WaitAll/cancellation/
// exception-propagation semantics.
internal static class DedicatedWorkerThread
{
    public static Task Run(Func<Task> work, string name)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work().GetAwaiter().GetResult();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = name
        };
        thread.Start();
        return tcs.Task;
    }
}
