using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SwiftList.Core.Services;

// Split out of UsnServicePipeServer to keep that file under the line-count limit.
internal static class PipeSecurityFactory
{
    public static PipeSecurity? Create()
    {
        try
        {
            var pipeSecurity = new PipeSecurity();
            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            pipeSecurity.AddAccessRule(new PipeAccessRule(
                everyoneSid,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow
            ));

            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            pipeSecurity.AddAccessRule(new PipeAccessRule(
                authenticatedUsersSid,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow
            ));
            Logger.Log("[PipeServer] PipeSecurity successfully configured.", LogLevel.Debug);
            return pipeSecurity;
        }
        catch (Exception ex)
        {
            Logger.Log($"[PipeServer] Failed to create PipeSecurity: {ex.Message}", LogLevel.Error);
            return null;
        }
    }
}
