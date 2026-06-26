using System.ServiceProcess;
using SwiftList.Core;

namespace SwiftList.Service;

public class UsnService : ServiceBase
{
    private SearchEngine? _engine;
    private UsnServicePipeServer? _pipeServer;

    public UsnService()
    {
        ServiceName = "SwiftListService";
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        Logger.Log("[UsnService] Service Starting...");
        try
        {
            ServicePluginLoader.LoadForService();
            _engine = new SearchEngine();
            _engine.InitializeOrLoadIndex(false);

            _pipeServer = new UsnServicePipeServer();
            _pipeServer.Start(_engine);
            Logger.Log("[UsnService] Service Started successfully.");
        }
        catch (Exception ex)
        {
            Logger.Log($"[UsnService] Failed to start service: {ex}", LogLevel.Error);
            Stop();
        }
    }

    protected override void OnStop()
    {
        Logger.Log("[UsnService] Service Stopping...");
        _pipeServer?.Stop();
        _pipeServer?.Dispose();
        _pipeServer = null;

        _engine?.Dispose();
        _engine = null;
        Logger.Log("[UsnService] Service Stopped.");
    }

    protected override void OnShutdown()
    {
        OnStop();
        base.OnShutdown();
    }

    internal void TestStart() => OnStart(Array.Empty<string>());
    internal void TestStop() => OnStop();
}
