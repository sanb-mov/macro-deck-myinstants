using Serilog;
using SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Api;
using SuchByte.MacroDeck.Sliders;

namespace SuchByte.MacroDeck.InternalPlugins.SonarPlugin;

/// <summary>
/// Exposes the SteelSeries Sonar volume channels as Macro Deck sliders.
///
/// Slider ids are stable and encode where the value lives in the Sonar API:
///   classic mode  -> "classic/{role}"
///   streamer mode -> "streamer/{mix}/{role}"   (mix = streaming | monitoring)
/// </summary>
public class SonarSliderProvider : ISliderProvider, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SonarSliderProvider));

    public const string Id = "steelseries.sonar";

    private readonly SonarApiClient _apiClient;
    private readonly System.Threading.Lock _stateLock = new();
    private readonly Dictionary<string, MacroDeckSlider> _sliders = new();

    private CancellationTokenSource? _pollCancellation;
    private SonarMode _mode = SonarMode.Classic;
    private bool _available;
    private bool _disposed;

    /// <summary>
    /// How often Sonar is polled for changes made outside of Macro Deck
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1000);

    public SonarSliderProvider(SonarApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public string ProviderId => Id;

    public string DisplayName => "SteelSeries Sonar";

    public bool IsAvailable => _available;

    /// <summary>
    /// The mixing mode Sonar is currently running in
    /// </summary>
    public SonarMode Mode => _mode;

    public event EventHandler<MacroDeckSlider>? SliderChanged;
    public event EventHandler? SlidersInvalidated;

    /// <summary>
    /// Raised when the connection state to Sonar changed
    /// </summary>
    public event EventHandler<bool>? AvailabilityChanged;

    /// <summary>
    /// Starts polling Sonar in the background
    /// </summary>
    public void StartPolling()
    {
        if (_pollCancellation is not null)
        {
            return;
        }

        _pollCancellation = new CancellationTokenSource();
        var token = _pollCancellation.Token;
        Task.Run(async () => await PollLoopAsync(token), token);
    }

    public void StopPolling()
    {
        _pollCancellation?.Cancel();
        _pollCancellation?.Dispose();
        _pollCancellation = null;
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Sonar poll iteration failed");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task<IReadOnlyList<MacroDeckSlider>> GetSlidersAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken, raiseEvents: false);

        lock (_stateLock)
        {
            return _sliders.Values
                .OrderBy(x => x.Position)
                .Select(x => x.Clone())
                .ToList();
        }
    }

    /// <summary>
    /// Reads the current state from Sonar and raises change events for everything that moved
    /// </summary>
    private async Task RefreshAsync(CancellationToken cancellationToken, bool raiseEvents = true)
    {
        var mode = await _apiClient.GetModeAsync(cancellationToken);
        var snapshot = await _apiClient.GetVolumeSettingsAsync(mode, cancellationToken);

        if (snapshot is null)
        {
            SetAvailable(false);
            return;
        }

        SetAvailable(true);

        var modeChanged = mode != _mode;
        _mode = mode;

        var changed = new List<MacroDeckSlider>();
        var structureChanged = modeChanged;

        lock (_stateLock)
        {
            if (modeChanged)
            {
                _sliders.Clear();
            }

            var position = 0;
            var seen = new HashSet<string>();

            foreach (var (channelKey, state) in OrderChannels(snapshot))
            {
                var sliderId = mode == SonarMode.Classic
                    ? $"classic/{channelKey}"
                    : $"streamer/{channelKey}";
                seen.Add(sliderId);

                if (!_sliders.TryGetValue(sliderId, out var slider))
                {
                    slider = CreateSlider(sliderId, channelKey, mode);
                    _sliders[sliderId] = slider;
                    structureChanged = true;
                }

                slider.Position = position++;

                // Round trip through the same precision the API accepts so tiny float
                // differences do not produce an endless stream of change events.
                var newValue = Math.Round(Math.Clamp(state.Volume, 0.0, 1.0), 2);
                if (Math.Abs(slider.Value - newValue) > 0.001 || slider.Muted != state.Muted)
                {
                    slider.Value = newValue;
                    slider.Muted = state.Muted;
                    changed.Add(slider.Clone());
                }
            }

            foreach (var removed in _sliders.Keys.Where(x => !seen.Contains(x)).ToList())
            {
                _sliders.Remove(removed);
                structureChanged = true;
            }
        }

        if (!raiseEvents)
        {
            return;
        }

        if (structureChanged)
        {
            SlidersInvalidated?.Invoke(this, EventArgs.Empty);
            return;
        }

        foreach (var slider in changed)
        {
            SliderChanged?.Invoke(this, slider);
        }
    }

    /// <summary>
    /// Master first, then the device roles, and in streamer mode grouped per sub mix
    /// </summary>
    private static IEnumerable<KeyValuePair<string, SonarChannelState>> OrderChannels(SonarVolumeSnapshot snapshot)
    {
        if (snapshot.Mode == SonarMode.Classic)
        {
            return Order(snapshot, "");
        }

        return Order(snapshot, "streaming/").Concat(Order(snapshot, "monitoring/"));
    }

    private static IEnumerable<KeyValuePair<string, SonarChannelState>> Order(SonarVolumeSnapshot snapshot,
        string prefix)
    {
        var keys = new List<string> { prefix + SonarRoles.Master };
        keys.AddRange(SonarRoles.Devices.Select(role => prefix + role));

        foreach (var key in keys)
        {
            if (snapshot.Channels.TryGetValue(key, out var state))
            {
                yield return new KeyValuePair<string, SonarChannelState>(key, state);
            }
        }
    }

    private static MacroDeckSlider CreateSlider(string sliderId, string channelKey, SonarMode mode)
    {
        var (mix, role) = SplitChannelKey(channelKey);

        return new MacroDeckSlider
        {
            SliderId = sliderId,
            ProviderId = Id,
            DisplayName = SonarRoles.GetDisplayName(role),
            ColorHex = SonarRoles.GetColorHex(role),
            SupportsMute = true,
            Group = mode == SonarMode.Classic
                ? null
                : mix == "streaming" ? "Streaming" : "Monitoring"
        };
    }

    /// <summary>
    /// Splits "streaming/game" into ("streaming", "game"). Classic keys have no mix part.
    /// </summary>
    private static (string? Mix, string Role) SplitChannelKey(string channelKey)
    {
        var separatorIndex = channelKey.IndexOf('/');
        return separatorIndex == -1
            ? (null, channelKey)
            : (channelKey[..separatorIndex], channelKey[(separatorIndex + 1)..]);
    }

    /// <summary>
    /// Parses a slider id back into the Sonar coordinates it addresses
    /// </summary>
    private static (SonarMode Mode, SonarStreamerMix Mix, string Role)? ParseSliderId(string sliderId)
    {
        var parts = sliderId.Split('/');
        return parts switch
        {
            ["classic", var role] => (SonarMode.Classic, SonarStreamerMix.Streaming, role),
            ["streamer", "streaming", var role] => (SonarMode.Streamer, SonarStreamerMix.Streaming, role),
            ["streamer", "monitoring", var role] => (SonarMode.Streamer, SonarStreamerMix.Monitoring, role),
            _ => null
        };
    }

    public async Task SetValueAsync(string sliderId, double value, CancellationToken cancellationToken = default)
    {
        if (ParseSliderId(sliderId) is not { } target)
        {
            return;
        }

        var (mode, mix, role) = target;

        if (mode == SonarMode.Classic)
        {
            await _apiClient.SetClassicVolumeAsync(role, value, cancellationToken);
        }
        else
        {
            await _apiClient.SetStreamerVolumeAsync(mix, role, value, cancellationToken);
        }

        UpdateLocal(sliderId, slider => slider.Value = Math.Round(Math.Clamp(value, 0.0, 1.0), 2));
    }

    public async Task SetMutedAsync(string sliderId, bool muted, CancellationToken cancellationToken = default)
    {
        if (ParseSliderId(sliderId) is not { } target)
        {
            return;
        }

        var (mode, mix, role) = target;

        if (mode == SonarMode.Classic)
        {
            await _apiClient.SetClassicMuteAsync(role, muted, cancellationToken);
        }
        else
        {
            await _apiClient.SetStreamerMuteAsync(mix, role, muted, cancellationToken);
        }

        UpdateLocal(sliderId, slider => slider.Muted = muted);
    }

    /// <summary>
    /// Returns the current value of a slider, or null when it is unknown
    /// </summary>
    public MacroDeckSlider? GetSlider(string sliderId)
    {
        lock (_stateLock)
        {
            return _sliders.TryGetValue(sliderId, out var slider) ? slider.Clone() : null;
        }
    }

    /// <summary>
    /// Keeps our own snapshot in sync after we pushed a value to Sonar, so the next poll
    /// does not report the change we just made. Broadcasting is done by the SliderManager.
    /// </summary>
    private void UpdateLocal(string sliderId, Action<MacroDeckSlider> mutate)
    {
        lock (_stateLock)
        {
            if (_sliders.TryGetValue(sliderId, out var slider))
            {
                mutate(slider);
            }
        }
    }

    private void SetAvailable(bool available)
    {
        if (_available == available)
        {
            return;
        }

        _available = available;
        Logger.Information("SteelSeries Sonar is {State}", available ? "available" : "unavailable");
        AvailabilityChanged?.Invoke(this, available);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopPolling();
        GC.SuppressFinalize(this);
    }
}
