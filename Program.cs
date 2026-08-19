namespace Moniswitch;

internal static class Program
{
    private const string InstanceName = @"Local\Moniswitch-9D8D2D0B-03AA-4E2B-92DA-97CA426149EC";
    private const string ActivationEventName = @"Local\Moniswitch-Activate-9D8D2D0B-03AA-4E2B-92DA-97CA426149EC";

    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(
            initiallyOwned: true,
            name: InstanceName,
            createdNew: out var isFirstInstance);
        using var activationEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivationEventName);

        if (!isFirstInstance)
        {
            activationEvent.Set();
            return;
        }

        ApplicationConfiguration.Initialize();

        try
        {
            using var context = new TrayApplicationContext();
            var activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                activationEvent,
                (_, timedOut) =>
                {
                    if (!timedOut)
                    {
                        context.ActivateMainWindow();
                    }
                },
                state: null,
                millisecondsTimeOutInterval: Timeout.Infinite,
                executeOnlyOnce: false);
            try
            {
                Application.Run(context);
            }
            finally
            {
                activationRegistration.Unregister(null);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Moniswitch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
