namespace Moniswitch;

internal static class InputRouteTransaction
{
    internal static bool NeedsRemoteRecovery(bool enabled, byte? selectedInput, byte? linuxInput,
        bool clientStable, bool remoteActive) =>
        enabled && linuxInput.HasValue && selectedInput == linuxInput && clientStable && !remoteActive;

    public static async Task RunAsync(
        Func<Task> switchInput, Func<Task> switchDisplay, Func<Task> restoreInput)
    {
        try
        {
            await switchInput();
            await switchDisplay();
        }
        catch
        {
            await restoreInput();
            throw;
        }
    }
}
