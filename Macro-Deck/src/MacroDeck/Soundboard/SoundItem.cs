namespace SuchByte.MacroDeck.Soundboard;

/// <summary>
/// A single sound button of myinstants.com
/// </summary>
public sealed record SoundItem(string Id, string Name, string Url);

/// <summary>
/// One page of sounds and whether another page follows it
/// </summary>
public sealed record SoundPage(IReadOnlyList<SoundItem> Sounds, bool HasMore);
