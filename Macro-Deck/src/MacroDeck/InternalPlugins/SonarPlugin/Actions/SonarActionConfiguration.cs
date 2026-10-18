using Newtonsoft.Json;
using SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Api;

namespace SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Actions;

/// <summary>
/// Serialized configuration shared by all Sonar actions
/// </summary>
public class SonarActionConfiguration
{
    /// <summary>
    /// Slider id of the target channel, e.g. "classic/game"
    /// </summary>
    public string SliderId { get; set; } = "classic/game";

    /// <summary>
    /// Absolute volume in percent, used by the "Set volume" action
    /// </summary>
    public int VolumePercent { get; set; } = 50;

    /// <summary>
    /// Relative change in percent, used by the "Change volume" action
    /// </summary>
    public int StepPercent { get; set; } = 5;

    /// <summary>
    /// Target mode, used by the "Switch mode" action
    /// </summary>
    public SonarMode Mode { get; set; } = SonarMode.Classic;

    public static SonarActionConfiguration Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SonarActionConfiguration();
        }

        try
        {
            return JsonConvert.DeserializeObject<SonarActionConfiguration>(json) ?? new SonarActionConfiguration();
        }
        catch
        {
            return new SonarActionConfiguration();
        }
    }

    public string Serialize()
    {
        return JsonConvert.SerializeObject(this);
    }
}
