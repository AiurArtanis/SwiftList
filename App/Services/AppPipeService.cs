using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using SwiftList.Core;
namespace SwiftList.App.Services
{
    public static class AppPipeService
    {
        private static bool _keepRunningPipeServer = true;

        public static void StopServer()
        {
            _keepRunningPipeServer = false;
        }

        public static async Task SendActivateSignalAsync(CancellationToken token = default)
        {
            string pipeName = $"SwiftList_App_Pipe_{Environment.UserName}";

            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);

                await client.ConnectAsync(500, token).ConfigureAwait(false);
                await PipeRequestBinarySerializer.WriteStringAsync(client, "ACTIVATE", token).ConfigureAwait(false);
            }

            catch (Exception ex)
            {
                Logger.Log($"Failed to send activation signal: {ex.Message}", SwiftList.Core.LogLevel.Error);
            }
        }

        public static Task StartPipeServerAsync()
        {
            return RunPipeServerAsync();
        }

        private static async Task RunPipeServerAsync()
        {
            string pipeName = $"SwiftList_App_Pipe_{Environment.UserName}";
            while (_keepRunningPipeServer)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync();
                    string msg = await PipeRequestBinarySerializer.ReadStringAsync(server);
                    if (msg == "ACTIVATE")
                    {
                        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var quickSearchWindow = System.Windows.Application.Current.MainWindow as QuickSearchWindow;
                            if (quickSearchWindow != null)
                            {
                                quickSearchWindow.ShowWindow();
                            }

                        }));
                    }
                }

                catch (Exception ex)
                {
                    Logger.Log($"[AppPipeService] Named pipe server error: {ex.Message}", SwiftList.Core.LogLevel.Error);

                    await Task.Delay(1000); // Prevent tight loop on error
                }
            }
        }
    }
}
