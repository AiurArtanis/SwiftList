using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SwiftList.Core.Indexer.Usn;
using SwiftList.Core.Indexer.NetworkDrive;

namespace SwiftList.Core
{
    public class SearchService : IDisposable
    {
        public UsnIndexer.IndexerStatus GetStatus()
        {
            string resp = SendPipeCommand(new SearchRequestMessage { Id = SearchRequestId.Status });
            if (string.IsNullOrEmpty(resp) || resp.StartsWith("ERROR"))
            {
                return new UsnIndexer.IndexerStatus { State = "error" };
            }
            try
            {
                return JsonSerializer.Deserialize<UsnIndexer.IndexerStatus>(resp) ?? new UsnIndexer.IndexerStatus();
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Failed to deserialize STATUS: {ex.Message}", SwiftList.Core.LogLevel.Error);
                return new UsnIndexer.IndexerStatus { State = "error" };
            }
        }

        public bool SearchStreaming(string query, int maxResults, int maxAppResults, string? directoryFilter, Action<SearchResult, bool> onResult, CancellationToken token = default)
        {
            var exclusionRules = ExclusionRuleSet.From(UserSettings.Load());
            int fileCandidateLimit = Math.Clamp(maxResults * 4, maxResults, 2000);

            var msg = new SearchRequestMessage();
            if (!string.IsNullOrEmpty(directoryFilter))
            {
                msg.Id = SearchRequestId.SearchDir;
                msg.Limit = fileCandidateLimit;
                msg.AppLimit = maxAppResults;
                msg.DirectoryFilter = directoryFilter;
                msg.Query = query;
            }
            else
            {
                msg.Id = SearchRequestId.Search;
                msg.Limit = fileCandidateLimit;
                msg.AppLimit = maxAppResults;
                msg.Query = query;
            }

            try
            {
                SendSearchPipeCommand(msg, (result, isApp) =>
                {
                    if (isApp || !exclusionRules.IsExcluded(result))
                        onResult(result, isApp);
                }, token);
                SearchNetworkDrives(query, fileCandidateLimit, directoryFilter, exclusionRules, onResult, token);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Streaming search failed: {ex.Message}", SwiftList.Core.LogLevel.Error);
                return SearchNetworkDrives(query, fileCandidateLimit, directoryFilter, exclusionRules, onResult, token);
            }
        }

        public void RefreshNetworkIndexes()
        {
            UserNetworkDriveSearch.Refresh();
        }

        public IReadOnlyList<NetworkIndexStatus> GetNetworkIndexStatuses()
        {
            return UserNetworkDriveSearch.GetStatuses();
        }

        public void InitializeOrLoadIndex(bool forceRebuild = false)
        {
            if (forceRebuild)
            {
                SendPipeCommand(new SearchRequestMessage { Id = SearchRequestId.Rebuild });
            }
        }

        public MachineSettings GetMachineSettings()
        {
            string resp = SendPipeCommand(new SearchRequestMessage { Id = SearchRequestId.GetMachineSettings });
            if (string.IsNullOrEmpty(resp) || resp.StartsWith("ERROR"))
                return new MachineSettings();

            try
            {
                return JsonSerializer.Deserialize<MachineSettings>(resp) ?? new MachineSettings();
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Failed to deserialize machine settings: {ex.Message}", SwiftList.Core.LogLevel.Error);
                return new MachineSettings();
            }
        }

        public bool SaveMachineSettings(MachineSettings settings)
        {
            string json = JsonSerializer.Serialize(settings);
            string resp = SendPipeCommand(new SearchRequestMessage { Id = SearchRequestId.SetMachineSettings, JsonSettings = json });
            return resp == "OK";
        }

        private string SendPipeCommand(SearchRequestMessage msg)
        {
            try
            {
                bool verboseLog = msg.Id != SearchRequestId.Search && msg.Id != SearchRequestId.SearchDir;
                if (verboseLog)
                    Logger.Log($"[PipeClient] Connecting to pipe for command: {msg.Id}...", LogLevel.Debug);

                using var pipe = new NamedPipeClientStream(".", "SwiftListPipe", PipeDirection.InOut);
                pipe.Connect(1000); // 1000ms timeout
                if (verboseLog)
                    Logger.Log("[PipeClient] Connected. Writing command...", LogLevel.Debug);
                
                SearchRequestBinarySerializer.WriteSearchRequest(pipe, msg);
                if (verboseLog)
                    Logger.Log("[PipeClient] Command written. Reading response...", LogLevel.Debug);
                
                string resp = PipeResponseBinarySerializer.ReadText(pipe);
                if (verboseLog)
                    Logger.Log($"[PipeClient] Response received (length: {resp.Length}).", LogLevel.Debug);

                return resp;
            }
            catch (Exception ex)
            {
                Logger.Log($"[PipeClient] SendPipeCommand failed for {msg.Id}: {ex.Message}", SwiftList.Core.LogLevel.Error);
                return "ERROR";
            }
        }

        private void SendSearchPipeCommand(SearchRequestMessage msg, Action<SearchResult, bool> onResult, CancellationToken token)
        {
            using var pipe = new NamedPipeClientStream(".", "SwiftListPipe", PipeDirection.InOut);
            pipe.Connect(1000);

            SearchRequestBinarySerializer.WriteSearchRequest(pipe, msg);

            SearchResponseBinarySerializer.Read(pipe, (result, isApp) =>
            {
                token.ThrowIfCancellationRequested();
                onResult(result, isApp);
            });
        }

        private static bool SearchNetworkDrives(string query, int maxResults, string? directoryFilter, ExclusionRuleSet exclusionRules, Action<SearchResult, bool> onResult, CancellationToken token)
        {
            try
            {
                var results = UserNetworkDriveSearch.Search(query, maxResults, token, directoryFilter);
                foreach (var result in results)
                {
                    token.ThrowIfCancellationRequested();
                    if (!exclusionRules.IsExcluded(result))
                        onResult(result, false);
                }

                return results.Count > 0;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Network drive search failed: {ex.Message}", SwiftList.Core.LogLevel.Error);
                return false;
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
