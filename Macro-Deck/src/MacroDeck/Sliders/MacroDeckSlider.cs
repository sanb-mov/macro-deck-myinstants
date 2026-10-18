using Newtonsoft.Json;

namespace SuchByte.MacroDeck.Sliders;

/// <summary>
/// Represents a single continuous control (e.g. a volume channel) which is provided
/// by an <see cref="ISliderProvider"/> and rendered as a slider on the clients.
/// </summary>
public class MacroDeckSlider
{
    /// <summary>
    /// Unique id of this slider. Must be stable across refreshes so clients can
    /// correlate updates with the slider they are showing.
    /// </summary>
    public string SliderId { get; set; } = "";

    /// <summary>
    /// Id of the <see cref="ISliderProvider"/> this slider belongs to
    /// </summary>
    public string ProviderId { get; set; } = "";

    /// <summary>
    /// Text shown above the slider
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Order of the slider inside the slider folder/view. Lower values come first.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Current value, normalized to 0.0 - 1.0
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// true = the channel is muted
    /// </summary>
    public bool Muted { get; set; }

    /// <summary>
    /// true = the client should render a mute toggle for this slider
    /// </summary>
    public bool SupportsMute { get; set; } = true;

    /// <summary>
    /// Accent color of the slider as #RRGGBB
    /// </summary>
    public string ColorHex { get; set; } = "#0067CD";

    /// <summary>
    /// Optional base64 encoded icon shown next to the slider
    /// </summary>
    public string? IconBase64 { get; set; }

    /// <summary>
    /// Optional group name. Sliders sharing a group are rendered next to each other
    /// (used by Sonar's streamer mode to separate Streaming from Monitoring).
    /// </summary>
    public string? Group { get; set; }

    [JsonIgnore] public double ValuePercent => Math.Round(Value * 100.0);

    public MacroDeckSlider Clone()
    {
        return (MacroDeckSlider)MemberwiseClone();
    }
}
