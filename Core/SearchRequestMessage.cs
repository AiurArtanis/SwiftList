namespace SwiftList.Core;

public enum SearchRequestId : byte
{
    Ping = 0,
    Status = 1,
    SubscribeStatus = 9,
    Rebuild = 2,
    GetMachineSettings = 3,
    SetMachineSettings = 4,
    Search = 5,
    SearchDir = 6,
    RebuildDrive = 7,
    DeleteDriveIndex = 8,
    Initialize = 10,
    GetFileMetadata = 11,
    ClearServiceLog = 12
}

public struct SearchRequestMessage
{
    public SearchRequestId Id { get; set; }
    public int Limit { get; set; }
    public int AppLimit { get; set; }
    public string? Query { get; set; }
    public string? DirectoryFilter { get; set; }
    public string? Drive { get; set; }
    public MachineSettings? MachineSettings { get; set; }
    public List<string>? DisabledAliasComponents { get; set; }
    public List<string>? FilePaths { get; set; }
}
