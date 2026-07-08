namespace SwiftList.Core.Indexer.NetworkDrive;

// Debounce-and-run queueing for refreshes, split out of Scheduler.cs to keep it under the project's line
// limit. QueueRefreshDrive debounces bursts of requests for the same drive (e.g. several watcher events
// in a row); StartRefreshDriveIfIdle/RefreshDriveLoop make sure only one refresh runs per drive at a time,
// re-running immediately if changes arrived while it was busy.
internal sealed partial class Scheduler
{
    public void QueueRefreshDrive(string drive, string reason)
    {
        CancellationTokenSource? oldDebounce = null;
        CancellationTokenSource debounce;
        lock (_gate)
        {
            if (_refreshCts == null || _refreshCts.IsCancellationRequested)
                return;

            if (_debounceCts.TryGetValue(drive, out oldDebounce))
                oldDebounce.Cancel();

            debounce = CancellationTokenSource.CreateLinkedTokenSource(_refreshCts.Token);
            _debounceCts[drive] = debounce;
        }

        try
        {
            oldDebounce?.Dispose();
        }
        catch { }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(reason == "configure" ? TimeSpan.Zero : TimeSpan.FromSeconds(2), debounce.Token).ConfigureAwait(false);
                StartRefreshDriveIfIdle(drive, reason, debounce.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                // Swallow exception caused by CancellationTokenSource being disposed to prevent UnobservedTaskException crash
            }
            finally
            {
                lock (_gate)
                {
                    if (_debounceCts.TryGetValue(drive, out var current) && ReferenceEquals(current, debounce))
                        _debounceCts.Remove(drive);
                }

                try
                {
                    debounce.Dispose();
                }
                catch { }
            }
        }, CancellationToken.None);
    }

    private void StartRefreshDriveIfIdle(string drive, string reason, CancellationToken token)
    {
        lock (_gate)
        {
            if (token.IsCancellationRequested || _refreshCts == null || _refreshCts.IsCancellationRequested)
                return;

            if (_refreshingDrives.Contains(drive))
            {
                _pendingRefreshDrives.Add(drive);
                return;
            }

            _refreshingDrives.Add(drive);
        }

        _ = Task.Run(() => RefreshDriveLoop(drive, reason, token), token);
    }

    private void RefreshDriveLoop(string drive, string reason, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                Logger.Log($"[NetworkIndexer] Refreshing {drive}: because {reason}");
                RefreshDrive(drive, token);

                lock (_gate)
                {
                    if (!_pendingRefreshDrives.Remove(drive))
                        break;
                }

                reason = "pending changes";
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // If Configure/StartRefresh replaced _refreshCts while this loop was still running (e.g. a
            // manual "rebuild" clicked while the drive's own initial scan was in flight), this loop's
            // `token` is now permanently cancelled, so the while condition above exits without ever
            // consuming a fresh pending request queued against the *new* token. Left alone, that request
            // is silently dropped and the drive's status stays stuck at "indexing" forever (nothing else
            // re-triggers a Manual-mode drive). Re-queue it through the normal path so it runs against
            // whatever token is current now.
            bool stillPending;
            lock (_gate)
            {
                _refreshingDrives.Remove(drive);
                stillPending = _pendingRefreshDrives.Remove(drive);
            }
            if (stillPending)
                QueueRefreshDrive(drive, "pending changes");
        }
    }
}
