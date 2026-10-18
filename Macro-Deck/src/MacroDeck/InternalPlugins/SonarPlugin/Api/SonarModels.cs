namespace SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Api;

/// <summary>
/// The two mixing modes Sonar can run in
/// </summary>
public enum SonarMode
{
    Classic,
    Streamer
}

/// <summary>
/// One of the two streamer sub mixes
/// </summary>
public enum SonarStreamerMix
{
    Streaming,
    Monitoring
}

/// <summary>
/// A single volume + mute pair as reported by /volumeSettings
/// </summary>
public class SonarChannelState
{
    public double Volume { get; set; }
    public bool Muted { get; set; }
}

/// <summary>
/// Flattened view of the Sonar volume settings for the currently active mode
/// </summary>
public class SonarVolumeSnapshot
{
    public SonarMode Mode { get; set; }

    /// <summary>
    /// Key = role ("master", "game", "chatRender", ...) for classic mode, or
    /// "{mix}/{role}" ("streaming/game", "monitoring/master", ...) for streamer mode.
    /// </summary>
    public Dictionary<string, SonarChannelState> Channels { get; } = new();
}

public static class SonarRoles
{
    public const string Master = "master";

    /// <summary>
    /// Roles as used in the Sonar REST paths, in the order Sonar itself shows them
    /// </summary>
    public static readonly string[] Devices = ["game", "chatRender", "media", "aux", "chatCapture"];

    /// <summary>
    /// Human readable names for the roles
    /// </summary>
    public static string GetDisplayName(string role)
    {
        return role switch
        {
            Master => "Master",
            "game" => "Game",
            "chatRender" => "Chat",
            "chatCapture" => "Mic",
            "media" => "Media",
            "aux" => "Aux",
            _ => role
        };
    }

    /// <summary>
    /// Accent color per role, matching the colors Sonar uses
    /// </summary>
    public static string GetColorHex(string role)
    {
        return role switch
        {
            Master => "#FFFFFF",
            "game" => "#F6A623",
            "chatRender" => "#4A90E2",
            "chatCapture" => "#7ED321",
            "media" => "#BD10E0",
            "aux" => "#50E3C2",
            _ => "#0067CD"
        };
    }
}

/// <summary>
/// Every addressable Sonar channel, independent of the mode Sonar is currently in.
/// Used to configure actions while Sonar is not running.
/// </summary>
public static class SonarChannels
{
    public record Channel(string SliderId, string DisplayName);

    public static IReadOnlyList<Channel> All { get; } = Build();

    private static List<Channel> Build()
    {
        var channels = new List<Channel>();
        var roles = new List<string> { SonarRoles.Master };
        roles.AddRange(SonarRoles.Devices);

        foreach (var role in roles)
        {
            channels.Add(new Channel($"classic/{role}", $"Classic - {SonarRoles.GetDisplayName(role)}"));
        }

        foreach (var role in roles)
        {
            channels.Add(new Channel($"streamer/streaming/{role}",
                $"Streamer / Streaming - {SonarRoles.GetDisplayName(role)}"));
        }

        foreach (var role in roles)
        {
            channels.Add(new Channel($"streamer/monitoring/{role}",
                $"Streamer / Monitoring - {SonarRoles.GetDisplayName(role)}"));
        }

        return channels;
    }

    public static string GetDisplayName(string sliderId)
    {
        return All.FirstOrDefault(x => x.SliderId == sliderId)?.DisplayName ?? sliderId;
    }
}
