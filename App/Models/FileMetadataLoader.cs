namespace SwiftList.App;

// Lazily stats a file/folder path once (Size + Created/Modified/Accessed timestamps together, a
// single I/O call) and marshals the result back to the UI thread. Extracted out of AppSearchResult
// to keep that file under the repo's line-count limit -- every AppSearchResult owns one of these.
internal sealed class FileMetadataLoader
{
    private static readonly SemaphoreSlim _semaphore = new(8);

    private long? _size;
    private DateTime? _dateCreated;
    private DateTime? _dateModified;
    private DateTime? _dateAccessed;
    private Task? _loadTask;

    public long Size => _size ?? 0;
    public DateTime DateCreated => _dateCreated ?? DateTime.MinValue;
    public DateTime DateModified => _dateModified ?? DateTime.MinValue;
    public DateTime DateAccessed => _dateAccessed ?? DateTime.MinValue;

    // Safe to call repeatedly (e.g. once per property read) -- only the first call actually starts
    // the stat; later calls return the same cached task. onLoaded fires once loading completes.
    public Task EnsureLoadedAsync(string fullPath, bool isDir, Action onLoaded) => _loadTask ??= LoadAsync(fullPath, isDir, onLoaded);

    private async Task LoadAsync(string fullPath, bool isDir, Action onLoaded)
    {
        await _semaphore.WaitAsync();
        try
        {
            long size = 0;
            var created = DateTime.MinValue;
            var modified = DateTime.MinValue;
            var accessed = DateTime.MinValue;

            await Task.Run(() =>
            {
                if (isDir)
                {
                    if (System.IO.Directory.Exists(fullPath))
                    {
                        var info = new System.IO.DirectoryInfo(fullPath);
                        created = info.CreationTime;
                        modified = info.LastWriteTime;
                        accessed = info.LastAccessTime;
                    }
                }
                else
                {
                    if (System.IO.File.Exists(fullPath))
                    {
                        var info = new System.IO.FileInfo(fullPath);
                        size = info.Length;
                        created = info.CreationTime;
                        modified = info.LastWriteTime;
                        accessed = info.LastAccessTime;
                    }
                }
            });

            var app = System.Windows.Application.Current;
            if (app != null)
            {
                await app.Dispatcher.InvokeAsync(() =>
                {
                    Apply(size, created, modified, accessed);
                    onLoaded();
                });
            }
            else
            {
                Apply(size, created, modified, accessed);
            }
        }
        catch
        {
            Apply(0, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void Apply(long size, DateTime created, DateTime modified, DateTime accessed)
    {
        _size = size;
        _dateCreated = created;
        _dateModified = modified;
        _dateAccessed = accessed;
    }
}
