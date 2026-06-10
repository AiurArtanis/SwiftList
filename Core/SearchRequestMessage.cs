namespace SwiftList.Core;

public enum SearchRequestId : byte
{
    Status = 1,
    Rebuild = 2,
    GetMachineSettings = 3,
    SetMachineSettings = 4,
    Search = 5,
    SearchDir = 6
}

public struct SearchRequestMessage
{
    public SearchRequestId Id { get; set; }
    public int Limit { get; set; }
    public int AppLimit { get; set; }
    public string? Query { get; set; }
    public string? DirectoryFilter { get; set; }
    public MachineSettings? MachineSettings { get; set; }
}
