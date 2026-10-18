using Serilog;
using SuchByte.MacroDeck.Server;

namespace SuchByte.MacroDeck.Sliders;

/// <summary>
/// Central registry for all <see cref="ISliderProvider"/> implementations.
/// Keeps a cache of the last known slider state and pushes changes to the clients.
/// </summary>
public static class SliderManager
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SliderManager));

    private static readonly Dictionary<string, ISliderProvider> ProvidersInternal = new();
    private static readonly Dictionary<string, List<MacroDeckSlider>> Cache = new();
    private static readonly Lock CacheLock = new();

    /// <summary>
    /// Raised when the list of registered providers changed
    /// </summary>
    public static event EventHandler? OnProvidersChanged;

    /// <summary>
    /// Raised when a single slider changed. Used by the Windows UI to refresh itself.
    /// </summary>
    public static event EventHandler<MacroDeckSlider>? OnSliderChanged;

    /// <summary>
    /// Raised when a provider invalidated its whole slider set
    /// </summary>
    public static event EventHandler<string>? OnSlidersInvalidated;

    public static IReadOnlyCollection<ISliderProvider> Providers => ProvidersInternal.Values.ToList();

    public static void RegisterProvider(ISliderProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.ProviderId))
        {
            Logger.Warning("Slider provider without a provider id was ignored");
            return;
        }

        if (!ProvidersInternal.TryAdd(provider.ProviderId, provider))
        {
            Logger.Warning("Slider provider {ProviderId} is already registered", provider.ProviderId);
            return;
        }

        provider.SliderChanged += ProviderOnSliderChanged;
        provider.SlidersInvalidated += ProviderOnSlidersInvalidated;

        Logger.Information("Registered slider provider {ProviderId}", provider.ProviderId);
        OnProvidersChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void UnregisterProvider(string providerId)
    {
        if (!ProvidersInternal.Remove(providerId, out var provider))
        {
            return;
        }

        provider.SliderChanged -= ProviderOnSliderChanged;
        provider.SlidersInvalidated -= ProviderOnSlidersInvalidated;

        lock (CacheLock)
        {
            Cache.Remove(providerId);
        }

        OnProvidersChanged?.Invoke(null, EventArgs.Empty);
    }

    public static ISliderProvider? GetProvider(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return ProvidersInternal.GetValueOrDefault(providerId);
    }

    /// <summary>
    /// Returns the sliders of a provider, refreshing them from the provider itself.
    /// Falls back to the cached state if the provider throws.
    /// </summary>
    public static async Task<IReadOnlyList<MacroDeckSlider>> GetSlidersAsync(string? providerId)
    {
        var provider = GetProvider(providerId);
        if (provider is null)
        {
            return Array.Empty<MacroDeckSlider>();
        }

        try
        {
            var sliders = await provider.GetSlidersAsync();
            lock (CacheLock)
            {
                Cache[provider.ProviderId] = sliders.Select(x => x.Clone()).ToList();
            }

            return sliders;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to get sliders from provider {ProviderId}", provider.ProviderId);
            return GetCachedSliders(provider.ProviderId);
        }
    }

    public static IReadOnlyList<MacroDeckSlider> GetCachedSliders(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return Array.Empty<MacroDeckSlider>();
        }

        lock (CacheLock)
        {
            return Cache.TryGetValue(providerId, out var sliders)
                ? sliders.Select(x => x.Clone()).ToList()
                : Array.Empty<MacroDeckSlider>();
        }
    }

    public static MacroDeckSlider? FindSlider(string? providerId, string sliderId)
    {
        return GetCachedSliders(providerId).FirstOrDefault(x => x.SliderId == sliderId);
    }

    /// <summary>
    /// Applies a value coming from a client or from the Windows UI
    /// </summary>
    public static async Task SetValueAsync(string providerId, string sliderId, double value)
    {
        var provider = GetProvider(providerId);
        if (provider is null)
        {
            return;
        }

        value = Math.Clamp(value, 0.0, 1.0);

        try
        {
            await provider.SetValueAsync(sliderId, value);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to set value of slider {SliderId}", sliderId);
            return;
        }

        UpdateCachedSlider(providerId, sliderId, slider => slider.Value = value);
    }

    /// <summary>
    /// Mutes/unmutes a slider coming from a client or from the Windows UI
    /// </summary>
    public static async Task SetMutedAsync(string providerId, string sliderId, bool muted)
    {
        var provider = GetProvider(providerId);
        if (provider is null)
        {
            return;
        }

        try
        {
            await provider.SetMutedAsync(sliderId, muted);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to set mute state of slider {SliderId}", sliderId);
            return;
        }

        UpdateCachedSlider(providerId, sliderId, slider => slider.Muted = muted);
    }

    private static void UpdateCachedSlider(string providerId, string sliderId, Action<MacroDeckSlider> mutate)
    {
        MacroDeckSlider? updated = null;

        lock (CacheLock)
        {
            if (Cache.TryGetValue(providerId, out var sliders))
            {
                var slider = sliders.FirstOrDefault(x => x.SliderId == sliderId);
                if (slider is not null)
                {
                    mutate(slider);
                    updated = slider.Clone();
                }
            }
        }

        if (updated is null)
        {
            return;
        }

        MacroDeckServer.UpdateSlider(updated);
        OnSliderChanged?.Invoke(null, updated);
    }

    private static void ProviderOnSliderChanged(object? sender, MacroDeckSlider slider)
    {
        if (sender is not ISliderProvider provider)
        {
            return;
        }

        lock (CacheLock)
        {
            if (!Cache.TryGetValue(provider.ProviderId, out var sliders))
            {
                sliders = new List<MacroDeckSlider>();
                Cache[provider.ProviderId] = sliders;
            }

            var index = sliders.FindIndex(x => x.SliderId == slider.SliderId);
            if (index == -1)
            {
                sliders.Add(slider.Clone());
            }
            else
            {
                sliders[index] = slider.Clone();
            }
        }

        MacroDeckServer.UpdateSlider(slider);
        OnSliderChanged?.Invoke(null, slider);
    }

    private static void ProviderOnSlidersInvalidated(object? sender, EventArgs e)
    {
        if (sender is not ISliderProvider provider)
        {
            return;
        }

        lock (CacheLock)
        {
            Cache.Remove(provider.ProviderId);
        }

        MacroDeckServer.UpdateSliderFolders(provider.ProviderId);
        OnSlidersInvalidated?.Invoke(null, provider.ProviderId);
    }
}
