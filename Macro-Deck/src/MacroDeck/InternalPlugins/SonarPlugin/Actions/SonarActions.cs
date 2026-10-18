using Serilog;
using SuchByte.MacroDeck.GUI;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Api;
using SuchByte.MacroDeck.InternalPlugins.SonarPlugin.GUI;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Sliders;

namespace SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Actions;

/// <summary>
/// Sets a Sonar channel to an absolute volume
/// </summary>
public class SonarSetVolumeAction : PluginAction
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SonarSetVolumeAction));

    public override string Name => "Set Sonar volume";
    public override string Description => "Sets a SteelSeries Sonar channel to a fixed volume";
    public override bool CanConfigure => true;

    public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
    {
        var provider = SonarPlugin.SliderProvider;
        if (provider is null)
        {
            return;
        }

        var configuration = SonarActionConfiguration.Deserialize(Configuration);
        Task.Run(async () =>
        {
            try
            {
                await SliderManager.SetValueAsync(SonarSliderProvider.Id,
                    configuration.SliderId,
                    configuration.VolumePercent / 100.0);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Set Sonar volume failed");
            }
        });
    }

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new SonarActionConfigurator(this, SonarActionConfiguratorMode.SetVolume);
    }
}

/// <summary>
/// Raises or lowers a Sonar channel by a fixed step
/// </summary>
public class SonarAdjustVolumeAction : PluginAction
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SonarAdjustVolumeAction));

    public override string Name => "Change Sonar volume";
    public override string Description => "Raises or lowers a SteelSeries Sonar channel by a fixed step";
    public override bool CanConfigure => true;

    public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
    {
        var provider = SonarPlugin.SliderProvider;
        if (provider is null)
        {
            return;
        }

        var configuration = SonarActionConfiguration.Deserialize(Configuration);
        Task.Run(async () =>
        {
            try
            {
                var current = provider.GetSlider(configuration.SliderId);
                if (current is null)
                {
                    return;
                }

                await SliderManager.SetValueAsync(SonarSliderProvider.Id,
                    configuration.SliderId,
                    current.Value + configuration.StepPercent / 100.0);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Change Sonar volume failed");
            }
        });
    }

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new SonarActionConfigurator(this, SonarActionConfiguratorMode.AdjustVolume);
    }
}

/// <summary>
/// Toggles the mute state of a Sonar channel
/// </summary>
public class SonarToggleMuteAction : PluginAction
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SonarToggleMuteAction));

    public override string Name => "Toggle Sonar mute";
    public override string Description => "Mutes or unmutes a SteelSeries Sonar channel";
    public override bool CanConfigure => true;

    public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
    {
        var provider = SonarPlugin.SliderProvider;
        if (provider is null)
        {
            return;
        }

        var configuration = SonarActionConfiguration.Deserialize(Configuration);
        Task.Run(async () =>
        {
            try
            {
                var current = provider.GetSlider(configuration.SliderId);
                if (current is null)
                {
                    return;
                }

                var muted = !current.Muted;
                await SliderManager.SetMutedAsync(SonarSliderProvider.Id, configuration.SliderId, muted);

                if (actionButton is not null)
                {
                    MacroDeckServer.SetState(actionButton, muted);
                    MacroDeckServer.UpdateState(actionButton);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Toggle Sonar mute failed");
            }
        });
    }

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new SonarActionConfigurator(this, SonarActionConfiguratorMode.ToggleMute);
    }
}

/// <summary>
/// Switches Sonar between classic and streamer mode
/// </summary>
public class SonarSwitchModeAction : PluginAction
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SonarSwitchModeAction));

    public override string Name => "Switch Sonar mode";
    public override string Description => "Switches SteelSeries Sonar between classic and streamer mode";
    public override bool CanConfigure => true;

    public override void Trigger(string clientId, ActionButton.ActionButton actionButton)
    {
        var apiClient = SonarPlugin.ApiClient;
        if (apiClient is null)
        {
            return;
        }

        var configuration = SonarActionConfiguration.Deserialize(Configuration);
        Task.Run(async () =>
        {
            try
            {
                await apiClient.SetModeAsync(configuration.Mode);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Switch Sonar mode failed");
            }
        });
    }

    public override ActionConfigControl GetActionConfigControl(ActionConfigurator actionConfigurator)
    {
        return new SonarActionConfigurator(this, SonarActionConfiguratorMode.SwitchMode);
    }
}
