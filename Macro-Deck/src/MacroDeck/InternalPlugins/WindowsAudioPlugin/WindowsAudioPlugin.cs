using Serilog;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Sliders;

namespace SuchByte.MacroDeck.InternalPlugins.WindowsAudioPlugin;

/// <summary>
/// Built in plugin that exposes the Windows volume mixer as sliders, so a slider
/// folder can control Windows audio instead of (or next to) SteelSeries Sonar.
/// </summary>
public class WindowsAudioPlugin : MacroDeckPlugin
{
    private static readonly ILogger Logger = Log.ForContext(typeof(WindowsAudioPlugin));

    internal override string Name => "Windows audio";
    internal override string Author => "Macro Deck";

    private static WindowsAudioSliderProvider? _sliderProvider;

    public static WindowsAudioSliderProvider? SliderProvider => _sliderProvider;

    public override void Enable()
    {
        if (_sliderProvider is not null)
        {
            return;
        }

        _sliderProvider = new WindowsAudioSliderProvider();
        SliderManager.RegisterProvider(_sliderProvider);
        _sliderProvider.StartPolling();
        Logger.Information("Windows audio plugin enabled");
    }
}
