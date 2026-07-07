using SwiftList.Core.Indexer.NetworkDrive;

namespace SwiftList.Core;

// Drive and settings admin pass-throughs, split out of SearchService.cs to stay under the repo's per-file
// line limit -- see the class-level comment there.
public partial class SearchService
{
    public void RefreshNetworkIndexes() => UserNetworkDriveSearch.Refresh();
    public void ConfigureNetworkIndexes() => UserNetworkDriveSearch.Configure();
    public bool RefreshNetworkDriveIndex(string drive) => UserNetworkDriveSearch.RefreshDrive(drive);
    public IReadOnlyList<NetworkIndexStatus> GetNetworkIndexStatuses() => UserNetworkDriveSearch.GetStatuses();
    public bool HasNetworkDriveCache(string drive) => UserNetworkDriveSearch.HasCache(drive);
    public IReadOnlyList<string> GetCachedNetworkDrives() => UserNetworkDriveSearch.GetCachedDrives();
    public void DeleteNetworkDriveCache(string drive) => UserNetworkDriveSearch.DeleteCache(drive);

    public async Task InitializeOrLoadIndexAsync(bool forceRebuild = false, CancellationToken token = default)
    {
        var requestId = forceRebuild ? SearchRequestId.Rebuild : SearchRequestId.Initialize;
        await SendPipeCommandAsync(new SearchRequestMessage { Id = requestId }, token).ConfigureAwait(false);
    }

    // service.log lives under the service's own (elevated/system) data directory, which the App
    // process cannot write to directly -- ask the service to truncate its own log file instead.
    public async Task<bool> ClearServiceLogAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.ClearServiceLog }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    public async Task<bool> RebuildDriveIndexAsync(string drive, CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.RebuildDrive, Drive = drive }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    public async Task<bool> DeleteDriveIndexAsync(string drive, CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.DeleteDriveIndex, Drive = drive }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    public async Task<MachineSettings> GetMachineSettingsAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.GetMachineSettings }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.MachineSettings && resp.MachineSettings != null) return resp.MachineSettings;
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] GetMachineSettings failed: {resp.Message}", LogLevel.Error);
        return new MachineSettings();
    }

    public async Task<bool> SaveMachineSettingsAsync(MachineSettings settings, CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.SetMachineSettings, MachineSettings = settings }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    // In-memory index lookup only (no disk I/O) -- paths the service isn't tracking are simply
    // absent from the result, not an error; the caller is expected to fall back to a live stat.
    public async Task<Dictionary<string, FileMetadataEntry>> GetFileMetadataBatchAsync(IReadOnlyList<string> paths, CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.GetFileMetadata, FilePaths = paths.ToList() }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.FileMetadata && resp.FileMetadata != null) return resp.FileMetadata;
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] GetFileMetadataBatch failed: {resp.Message}", LogLevel.Error);
        return new Dictionary<string, FileMetadataEntry>(StringComparer.OrdinalIgnoreCase);
    }
}
