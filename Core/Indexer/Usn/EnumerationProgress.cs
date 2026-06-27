namespace SwiftList.Core.Indexer.Usn;

internal sealed class EnumerationProgress
{
    private readonly Action<int, int>? _onProgress;
    private int _files;
    private int _dirs;
    private int _nextReport = 32768;

    public EnumerationProgress(Action<int, int>? onProgress) => _onProgress = onProgress;

    public void Add(bool isDirectory, int totalItems)
    {
        if (isDirectory) _dirs++;
        else _files++;

        if (totalItems < _nextReport)
            return;

        Report();
        _nextReport = totalItems + 32768;
    }

    public void Report() => _onProgress?.Invoke(_files, _dirs);
}
