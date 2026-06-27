using SwiftList.Core;

namespace SwiftList.App.Services;

internal static class AppStartupServiceBootstrapper
{
    public static void EnsureServiceStarted() => _ = Task.Run(async () =>
                                                      {
                                                          using var searchService = new SearchService();
                                                          try
                                                          {
                                                              if (await searchService.PingAsync().ConfigureAwait(false))
                                                              {
                                                                  Logger.Log("[AppStartupServiceBootstrapper] Service already reachable on app startup.");
                                                                  return;
                                                              }
                                                          }
                                                          catch (Exception ex)
                                                          {
                                                              Logger.Log($"[AppStartupServiceBootstrapper] Service ping failed: {ex.Message}", LogLevel.Warn);
                                                          }

                                                          Logger.Log("[AppStartupServiceBootstrapper] Service unavailable on app startup. Attempting silent install/start.");
                                                          ServiceInstallManager.SilentInstall(() => Logger.Log("[AppStartupServiceBootstrapper] Silent install/start attempt completed."));
                                                      });
}
