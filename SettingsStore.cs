using System.Text.Json;

namespace Moniswitch;

internal sealed class AppSettings
{
    public int Version { get; set; } = 5;
    public bool PrivacyView { get; set; } = true;
    public string? QuickToggleMonitorId { get; set; }
    public byte? QuickToggleInputA { get; set; }
    public byte? QuickToggleInputB { get; set; }
    public HotkeySettings Hotkey { get; set; } = new();
    public List<SwitchProfile> Profiles { get; set; } = [];
    public InputSharingSettings InputSharing { get; set; } = new();
    public LanCanvasSettings LanCanvas { get; set; } = new();
}

internal sealed class LanCanvasSettings
{
    public bool Enabled { get; set; }
    public string DisplayTarget { get; set; } = LanCanvasController.AllDisplaysTarget;
    public string LinuxHost { get; set; } = string.Empty;
    public string LinuxUser { get; set; } = string.Empty;
    public string? SshKeyPath { get; set; }
    public string? MoonlightExecutablePath { get; set; }
    public int FramesPerSecond { get; set; } = 60;
    public int BitrateKbps { get; set; } = 60000;
}

internal sealed class InputSharingSettings
{
    public bool Enabled { get; set; }
    public string WindowsScreenName { get; set; } = "windows-pc";
    public string LinuxScreenName { get; set; } = "linux-pc";
    public string? WindowsProfileId { get; set; }
    public string? LinuxProfileId { get; set; }
    public string? DeskflowExecutablePath { get; set; }
}

internal sealed class SwitchProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Untitled profile";
    public Dictionary<string, byte> Assignments { get; set; } = [];
}

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _gate = new();

    public SettingsStore(string? directoryPath = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DirectoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? Path.Combine(localAppData, "Moniswitch")
            : Path.GetFullPath(directoryPath);
        FilePath = Path.Combine(DirectoryPath, "settings.json");
        Current = Load();
    }

    public string DirectoryPath { get; }
    public string FilePath { get; }
    public AppSettings Current { get; }

    public void Save()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions)
                    ?? new AppSettings();
                settings.Hotkey ??= new HotkeySettings();
                settings.InputSharing ??= new InputSharingSettings();
                settings.LanCanvas ??= new LanCanvasSettings();
                settings.Version = Math.Max(settings.Version, 5);
                return settings;
            }
        }
        catch
        {
            // A malformed user-edited file should never prevent the app from
            // opening. Saving from the UI replaces it with a valid document.
        }

        return new AppSettings();
    }
}
