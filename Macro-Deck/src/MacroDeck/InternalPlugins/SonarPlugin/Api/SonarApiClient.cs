using System.Globalization;
using System.IO;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Serilog;

namespace SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Api;

/// <summary>
/// Talks to the local SteelSeries Sonar REST API.
///
/// Sonar binds to a random port on every start, so the base address has to be discovered:
///   1. %ProgramData%\SteelSeries\GG\coreProps.json -> ggEncryptedAddress
///   2. GET https://{ggEncryptedAddress}/subApps (self signed certificate)
///   3. subApps.sonar.metadata.webServerAddress -> base address of the Sonar API
/// </summary>
public class SonarApiClient : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SonarApiClient));

    private static readonly string CorePropsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SteelSeries",
        "GG",
        "coreProps.json");

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);
    private string? _baseAddress;
    private bool _disposed;

    public SonarApiClient()
    {
        // The GG server uses a self signed certificate that is regenerated on every run,
        // so it cannot be pinned. The connection never leaves localhost.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    /// <summary>
    /// The discovered Sonar base address, or null when Sonar was not reachable yet
    /// </summary>
    public string? BaseAddress => _baseAddress;

    public bool IsConnected => !string.IsNullOrWhiteSpace(_baseAddress);

    /// <summary>
    /// Discovers the Sonar base address. Returns false when GG or Sonar is not running.
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _discoveryLock.WaitAsync(cancellationToken);
        try
        {
            _baseAddress = null;

            if (!File.Exists(CorePropsPath))
            {
                Logger.Debug("SteelSeries GG coreProps.json not found at {Path}", CorePropsPath);
                return false;
            }

            var corePropsJson = await File.ReadAllTextAsync(CorePropsPath, cancellationToken);
            var ggAddress = JObject.Parse(corePropsJson)["ggEncryptedAddress"]?.ToString();
            if (string.IsNullOrWhiteSpace(ggAddress))
            {
                Logger.Debug("coreProps.json does not contain a ggEncryptedAddress");
                return false;
            }

            var subAppsJson = await _httpClient.GetStringAsync($"https://{ggAddress}/subApps", cancellationToken);
            var sonar = JObject.Parse(subAppsJson)["subApps"]?["sonar"];
            if (sonar is null)
            {
                Logger.Debug("GG does not report a sonar sub app");
                return false;
            }

            if (sonar["isRunning"]?.ToObject<bool>() != true || sonar["isReady"]?.ToObject<bool>() != true)
            {
                Logger.Debug("Sonar is not running or not ready yet");
                return false;
            }

            var webServerAddress = sonar["metadata"]?["webServerAddress"]?.ToString();
            if (string.IsNullOrWhiteSpace(webServerAddress))
            {
                Logger.Debug("Sonar does not report a webServerAddress");
                return false;
            }

            _baseAddress = webServerAddress.TrimEnd('/');
            Logger.Information("Connected to SteelSeries Sonar at {Address}", _baseAddress);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to discover the SteelSeries Sonar API");
            _baseAddress = null;
            return false;
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    /// <summary>
    /// Ensures a base address is known, re-discovering it when Sonar restarted
    /// </summary>
    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        return IsConnected || await ConnectAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the currently selected mixing mode
    /// </summary>
    public async Task<SonarMode> GetModeAsync(CancellationToken cancellationToken = default)
    {
        var response = await GetStringAsync("/mode", cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
        {
            return SonarMode.Classic;
        }

        // Sonar answers with a quoted string ("classic"/"stream") on current builds, while
        // the OpenAPI spec declares an int enum (0 = classic, 1 = stream). Accept both.
        var value = response.Trim().Trim('"');
        return value.Contains("stream", StringComparison.OrdinalIgnoreCase) || value == "1"
            ? SonarMode.Streamer
            : SonarMode.Classic;
    }

    /// <summary>
    /// Switches Sonar between classic and streamer mode
    /// </summary>
    public Task SetModeAsync(SonarMode mode, CancellationToken cancellationToken = default)
    {
        return PutAsync($"/mode/{(mode == SonarMode.Classic ? "classic" : "stream")}", cancellationToken);
    }

    /// <summary>
    /// Reads the volume settings of the given mode and flattens them into a snapshot
    /// </summary>
    public async Task<SonarVolumeSnapshot?> GetVolumeSettingsAsync(SonarMode mode,
        CancellationToken cancellationToken = default)
    {
        var path = mode == SonarMode.Classic ? "/volumeSettings/classic" : "/volumeSettings/streamer";
        var json = await GetStringAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var root = JObject.Parse(json);
        var snapshot = new SonarVolumeSnapshot { Mode = mode };

        if (mode == SonarMode.Classic)
        {
            AddChannel(snapshot, SonarRoles.Master, root["masters"]?["classic"]);
            foreach (var role in SonarRoles.Devices)
            {
                AddChannel(snapshot, role, root["devices"]?[role]?["classic"]);
            }
        }
        else
        {
            foreach (var mix in new[] { SonarStreamerMix.Streaming, SonarStreamerMix.Monitoring })
            {
                var mixKey = MixKey(mix);
                AddChannel(snapshot, $"{mixKey}/{SonarRoles.Master}", root["masters"]?["stream"]?[mixKey]);
                foreach (var role in SonarRoles.Devices)
                {
                    AddChannel(snapshot, $"{mixKey}/{role}", root["devices"]?[role]?["stream"]?[mixKey]);
                }
            }
        }

        return snapshot;
    }

    private static void AddChannel(SonarVolumeSnapshot snapshot, string key, JToken? token)
    {
        if (token is null)
        {
            return;
        }

        snapshot.Channels[key] = new SonarChannelState
        {
            Volume = token["volume"]?.ToObject<double>() ?? 0,
            Muted = token["muted"]?.ToObject<bool>() ?? token["isMuted"]?.ToObject<bool>() ?? false
        };
    }

    /// <summary>
    /// Sets the volume (0.0 - 1.0) of a classic mode role
    /// </summary>
    public Task SetClassicVolumeAsync(string role, double volume, CancellationToken cancellationToken = default)
    {
        var segment = role == SonarRoles.Master ? "Master" : role;
        return PutAsync($"/volumeSettings/classic/{segment}/Volume/{Format(volume)}", cancellationToken);
    }

    /// <summary>
    /// Mutes/unmutes a classic mode role
    /// </summary>
    public Task SetClassicMuteAsync(string role, bool muted, CancellationToken cancellationToken = default)
    {
        var segment = role == SonarRoles.Master ? "Master" : role;
        return PutAsync($"/volumeSettings/classic/{segment}/Mute/{Format(muted)}", cancellationToken);
    }

    /// <summary>
    /// Sets the volume (0.0 - 1.0) of a streamer mode role in the given sub mix
    /// </summary>
    public Task SetStreamerVolumeAsync(SonarStreamerMix mix,
        string role,
        double volume,
        CancellationToken cancellationToken = default)
    {
        var mixKey = MixKey(mix);
        var path = role == SonarRoles.Master
            ? $"/volumeSettings/streamer/{mixKey}/master/volume/{Format(volume)}"
            : $"/volumeSettings/streamer/{mixKey}/{role}/volume/{Format(volume)}";
        return PutAsync(path, cancellationToken);
    }

    /// <summary>
    /// Mutes/unmutes a streamer mode role in the given sub mix
    /// </summary>
    public Task SetStreamerMuteAsync(SonarStreamerMix mix,
        string role,
        bool muted,
        CancellationToken cancellationToken = default)
    {
        var mixKey = MixKey(mix);
        var path = role == SonarRoles.Master
            ? $"/volumeSettings/streamer/{mixKey}/master/isMuted/{Format(muted)}"
            : $"/volumeSettings/streamer/{mixKey}/{role}/isMuted/{Format(muted)}";
        return PutAsync(path, cancellationToken);
    }

    public static string MixKey(SonarStreamerMix mix)
    {
        return mix == SonarStreamerMix.Streaming ? "streaming" : "monitoring";
    }

    private static string Format(double value)
    {
        return Math.Clamp(value, 0.0, 1.0).ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string Format(bool value)
    {
        return value ? "true" : "false";
    }

    private async Task<string?> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        if (!await EnsureConnectedAsync(cancellationToken))
        {
            return null;
        }

        try
        {
            using var response = await _httpClient.GetAsync(_baseAddress + path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The request reached Sonar but it refused it. The connection is still good,
                // so keep the discovered address.
                Logger.Debug("GET {Path} returned {StatusCode}", path, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "GET {Path} failed, dropping the cached Sonar address", path);
            _baseAddress = null;
            return null;
        }
    }

    private async Task PutAsync(string path, CancellationToken cancellationToken)
    {
        if (!await EnsureConnectedAsync(cancellationToken))
        {
            return;
        }

        try
        {
            using var content = new StringContent(string.Empty);
            content.Headers.ContentLength = 0;
            using var response = await _httpClient.PutAsync(_baseAddress + path, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Sonar answers 500 {"error":"Cannot be called in current mode"} when the
                // request targets the mixing mode it is not currently running in. That is a
                // rejected request, not a lost connection, so the address stays cached.
                Logger.Debug("PUT {Path} returned {StatusCode}", path, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "PUT {Path} failed, dropping the cached Sonar address", path);
            _baseAddress = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
        _discoveryLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
