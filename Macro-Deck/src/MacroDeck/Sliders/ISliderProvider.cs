namespace SuchByte.MacroDeck.Sliders;

/// <summary>
/// Implement this to expose a set of sliders (e.g. audio channels) to Macro Deck.
/// Register the implementation with <see cref="SliderManager.RegisterProvider"/>.
/// </summary>
public interface ISliderProvider
{
    /// <summary>
    /// Unique, stable id of the provider (e.g. "steelseries.sonar")
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Name shown in the UI when the user picks a provider for a slider folder
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// false = the backing software is not reachable at the moment
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Returns the current sliders. Called when a client opens a slider folder and
    /// whenever the view is refreshed.
    /// </summary>
    Task<IReadOnlyList<MacroDeckSlider>> GetSlidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a new value (0.0 - 1.0) to the slider with the given id
    /// </summary>
    Task SetValueAsync(string sliderId, double value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mutes or unmutes the slider with the given id
    /// </summary>
    Task SetMutedAsync(string sliderId, bool muted, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when a slider changed outside of Macro Deck (e.g. the user moved it in
    /// the vendor software). Macro Deck pushes the change to all connected clients.
    /// </summary>
    event EventHandler<MacroDeckSlider>? SliderChanged;

    /// <summary>
    /// Raised when the whole set of sliders changed (e.g. Sonar switched between
    /// classic and streamer mode). Macro Deck re-requests all sliders.
    /// </summary>
    event EventHandler? SlidersInvalidated;
}
