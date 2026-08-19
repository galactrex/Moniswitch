namespace Moniswitch;

internal static class AppIcon
{
    public static Icon Create()
    {
        try
        {
            var associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (associated is not null)
            {
                using (associated)
                {
                    return (Icon)associated.Clone();
                }
            }
        }
        catch
        {
            // Development builds can run before the generated icon exists.
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
