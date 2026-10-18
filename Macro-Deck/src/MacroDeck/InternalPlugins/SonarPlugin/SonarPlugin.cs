using Serilog;
using SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Actions;
using SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Api;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Sliders;

namespace SuchByte.MacroDeck.InternalPlugins.SonarPlugin;

/// <summary>
/// Built in plugin that exposes the SteelSeries Sonar volume channels both as
/// sliders (slider folders and the Sonar view in the main window) and as actions
/// that can be put on regular deck buttons.
/// </summary>
public class SonarPlugin : MacroDeckPlugin
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SonarPlugin));

    internal override string Name => "SteelSeries Sonar";
    internal override string Author => "Macro Deck";

    private static SonarApiClient? _apiClient;
    private static SonarSliderProvider? _sliderProvider;

    /// <summary>
    /// The provider instance, or null while the plugin is not enabled
    /// </summary>
    public static SonarSliderProvider? SliderProvider => _sliderProvider;

    public static SonarApiClient? ApiClient => _apiClient;

    public override void Enable()
    {
        if (_sliderProvider is null)
        {
            _apiClient = new SonarApiClient();
            _sliderProvider = new SonarSliderProvider(_apiClient);
            SliderManager.RegisterProvider(_sliderProvider);
            _sliderProvider.StartPolling();
            Logger.Information("SteelSeries Sonar plugin enabled");
        }

        Actions = new List<PluginAction>
        {
            new SonarSetVolumeAction(),
            new SonarAdjustVolumeAction(),
            new SonarToggleMuteAction(),
            new SonarSwitchModeAction()
        };
    }
}
