using System;
using System.Diagnostics;
using System.IO.Pipes;
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

        public static void SendActivateSignal()
        {
            string pipeName = $"SwiftList_App_Pipe_{Environment.UserName}";
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                client.Connect(500); // 500ms timeout
                PipeRequestBinarySerializer.Write(client, "ACTIVATE");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to send activation signal: {ex.Message}");
            }
        }

        public static async void StartPipeServer()
        {
            string pipeName = $"SwiftList_App_Pipe_{Environment.UserName}";
            while (_keepRunningPipeServer)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();

                    string msg = PipeRequestBinarySerializer.Read(server);
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
                    Logger.Log($"[AppPipeService] Named pipe server error: {ex.Message}");
                    await Task.Delay(1000); // Prevent tight loop on error
                }
            }
        }
    }
}
