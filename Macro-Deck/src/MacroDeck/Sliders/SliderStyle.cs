namespace SuchByte.MacroDeck.Sliders;

/// <summary>
/// Visual style a slider folder is rendered with. The value travels to the clients
/// so a folder looks the same on the PC, the web client and the Android app.
/// </summary>
public enum SliderStyle
{
    /// <summary>
    /// Narrow track with a small pill shaped thumb, a percentage and a mute button.
    /// </summary>
    Classic = 0,

    /// <summary>
    /// Wide pill shaped capsule; the fill rises inside it and there is no thumb.
    /// </summary>
    Capsule = 1,

    /// <summary>
    /// Wide track with a large ring shaped knob riding on top of the fill.
    /// </summary>
    Knob = 2
}
