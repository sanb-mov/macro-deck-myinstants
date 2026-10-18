using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using SuchByte.MacroDeck.InternalPlugins.WindowsAudioPlugin.Api;
using SuchByte.MacroDeck.Sliders;

namespace SuchByte.MacroDeck.InternalPlugins.WindowsAudioPlugin;

/// <summary>
/// Exposes the Windows volume mixer as Macro Deck sliders: the default output and
/// input devices, plus one slider per application that is currently playing audio.
///
/// Slider ids:
///   "device/render"  - default playback device
///   "device/capture" - default recording device
///   "app/{processName}" - all sessions of that process, moved together
/// </summary>
public class WindowsAudioSliderProvider : ISliderProvider, IDisposable
{
    private static readonly ILogger Logger = Log.ForContext(typeof(WindowsAudioSliderProvider));

    public const string Id = "windows.audio";

    private const string RenderSliderId = "device/render";
    private const string CaptureSliderId = "device/capture";
    private const string AppPrefix = "app/";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1000);

    private readonly System.Threading.Lock _stateLock = new();
    private readonly Dictionary<string, MacroDeckSlider> _sliders = new();

    private CancellationTokenSource? _pollCancellation;
    private bool _available;
    private bool _disposed;

    public string ProviderId => Id;

    public string DisplayName => "Windows audio";

    public bool IsAvailable => _available;

    public event EventHandler<MacroDeckSlider>? SliderChanged;
    public event EventHandler? SlidersInvalidated;

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
                Refresh(raiseEvents: true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Windows audio poll iteration failed");
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

    public Task<IReadOnlyList<MacroDeckSlider>> GetSlidersAsync(CancellationToken cancellationToken = default)
    {
        Refresh(raiseEvents: false);

        lock (_stateLock)
        {
            IReadOnlyList<MacroDeckSlider> result = _sliders.Values
                .OrderBy(x => x.Position)
                .Select(x => x.Clone())
                .ToList();
            return Task.FromResult(result);
        }
    }

    // ------------------------------------------------------------------ reading

    private void Refresh(bool raiseEvents)
    {
        var snapshot = ReadSnapshot();
        if (snapshot is null)
        {
            SetAvailable(false);
            return;
        }

        SetAvailable(true);

        var changed = new List<MacroDeckSlider>();
        var structureChanged = false;

        lock (_stateLock)
        {
            var seen = new HashSet<string>();
            var position = 0;

            foreach (var entry in snapshot)
            {
                seen.Add(entry.SliderId);

                if (!_sliders.TryGetValue(entry.SliderId, out var slider))
                {
                    slider = new MacroDeckSlider
                    {
                        SliderId = entry.SliderId,
                        ProviderId = Id,
                        SupportsMute = true
                    };
                    _sliders[entry.SliderId] = slider;
                    structureChanged = true;
                }

                if (slider.DisplayName != entry.DisplayName ||
                    slider.Group != entry.Group ||
                    slider.ColorHex != entry.ColorHex)
                {
                    slider.DisplayName = entry.DisplayName;
                    slider.Group = entry.Group;
                    slider.ColorHex = entry.ColorHex;
                    structureChanged = true;
                }

                slider.Position = position++;

                var value = Math.Round(Math.Clamp(entry.Volume, 0.0, 1.0), 2);
                if (Math.Abs(slider.Value - value) > 0.001 || slider.Muted != entry.Muted)
                {
                    slider.Value = value;
                    slider.Muted = entry.Muted;
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

    private sealed record Entry(string SliderId, string DisplayName, string Group, string ColorHex,
        double Volume, bool Muted);

    private List<Entry>? ReadSnapshot()
    {
        CoreAudio.IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumerator();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Could not create the Core Audio device enumerator");
            return null;
        }

        var entries = new List<Entry>();
        try
        {
            AddDevice(enumerator, entries, CoreAudio.EDataFlow.Render, RenderSliderId, "#4A90E2");
            AddDevice(enumerator, entries, CoreAudio.EDataFlow.Capture, CaptureSliderId, "#7ED321");
            AddSessions(enumerator, entries);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Reading the Windows audio state failed");
            return entries.Count > 0 ? entries : null;
        }
        finally
        {
            Release(enumerator);
        }

        return entries;
    }

    private static void AddDevice(CoreAudio.IMMDeviceEnumerator enumerator,
        List<Entry> entries,
        CoreAudio.EDataFlow flow,
        string sliderId,
        string colorHex)
    {
        CoreAudio.IMMDevice? device = null;
        object? volumeObject = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(flow, CoreAudio.ERole.Multimedia, out device) != 0 ||
                device is null)
            {
                return;
            }

            var iid = CoreAudio.IID_IAudioEndpointVolume;
            if (device.Activate(ref iid, CoreAudio.ClsCtxAll, IntPtr.Zero, out volumeObject) != 0)
            {
                return;
            }

            var endpoint = (CoreAudio.IAudioEndpointVolume)volumeObject;
            endpoint.GetMasterVolumeLevelScalar(out var level);
            endpoint.GetMute(out var muted);

            entries.Add(new Entry(sliderId,
                flow == CoreAudio.EDataFlow.Render ? "Output" : "Mic",
                "Devices",
                colorHex,
                level,
                muted));
        }
        finally
        {
            Release(volumeObject);
            Release(device);
        }
    }

    private static void AddSessions(CoreAudio.IMMDeviceEnumerator enumerator, List<Entry> entries)
    {
        CoreAudio.IMMDevice? device = null;
        object? managerObject = null;
        CoreAudio.IAudioSessionEnumerator? sessions = null;

        try
        {
            if (enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Render,
                    CoreAudio.ERole.Multimedia,
                    out device) != 0 ||
                device is null)
            {
                return;
            }

            var iid = CoreAudio.IID_IAudioSessionManager2;
            if (device.Activate(ref iid, CoreAudio.ClsCtxAll, IntPtr.Zero, out managerObject) != 0)
            {
                return;
            }

            var manager = (CoreAudio.IAudioSessionManager2)managerObject;
            if (manager.GetSessionEnumerator(out sessions) != 0 || sessions is null)
            {
                return;
            }

            if (sessions.GetCount(out var count) != 0)
            {
                return;
            }

            // Several sessions can belong to one process (e.g. a browser per tab);
            // they are merged into a single slider that carries the loudest of them.
            var byProcess = new Dictionary<string, Entry>();

            for (var i = 0; i < count; i++)
            {
                CoreAudio.IAudioSessionControl? control = null;
                try
                {
                    if (sessions.GetSession(i, out control) != 0 || control is null)
                    {
                        continue;
                    }

                    if (control is not CoreAudio.IAudioSessionControl2 control2)
                    {
                        continue;
                    }

                    if (control2.GetState(out var state) == 0 && state == CoreAudio.AudioSessionStateExpired)
                    {
                        continue;
                    }

                    // S_OK means this is the system sounds session; skip it.
                    if (control2.IsSystemSoundsSession() == 0)
                    {
                        continue;
                    }

                    if (control2.GetProcessId(out var processId) != 0 || processId <= 0)
                    {
                        continue;
                    }

                    var processName = GetProcessName(processId);
                    if (processName is null)
                    {
                        continue;
                    }

                    if (control is not CoreAudio.ISimpleAudioVolume volume)
                    {
                        continue;
                    }

                    volume.GetMasterVolume(out var level);
                    volume.GetMute(out var muted);

                    var sliderId = AppPrefix + processName.ToLowerInvariant();
                    if (byProcess.TryGetValue(sliderId, out var existing) && existing.Volume >= level)
                    {
                        continue;
                    }

                    byProcess[sliderId] = new Entry(sliderId,
                        Shorten(processName),
                        "Apps",
                        ColorForName(processName),
                        level,
                        muted);
                }
                catch
                {
                    // A session can disappear between enumeration and read; skip it.
                }
                finally
                {
                    Release(control);
                }
            }

            entries.AddRange(byProcess.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            Release(sessions);
            Release(managerObject);
            Release(device);
        }
    }

    private static string? GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Device names in particular are long ("Speakers (Realtek(R) Audio)"); the slider
    /// is a narrow column, so keep only what fits.
    /// </summary>
    private static string Shorten(string name)
    {
        var trimmed = name.Trim();
        var parenthesis = trimmed.IndexOf(" (", StringComparison.Ordinal);
        if (parenthesis > 3)
        {
            trimmed = trimmed[..parenthesis];
        }

        return trimmed.Length <= 14 ? trimmed : trimmed[..14];
    }

    /// <summary>
    /// Stable colour per application so a slider keeps its colour between restarts
    /// </summary>
    private static string ColorForName(string name)
    {
        string[] palette =
        [
            "#F6A623", "#4A90E2", "#7ED321", "#BD10E0", "#50E3C2", "#E2574A", "#F8E71C", "#9013FE"
        ];

        var hash = 0;
        foreach (var c in name.ToLowerInvariant())
        {
            hash = unchecked(hash * 31 + c);
        }

        return palette[Math.Abs(hash) % palette.Length];
    }

    // ------------------------------------------------------------------ writing

    public Task SetValueAsync(string sliderId, double value, CancellationToken cancellationToken = default)
    {
        Apply(sliderId,
            endpoint =>
            {
                var context = Guid.Empty;
                endpoint.SetMasterVolumeLevelScalar((float)value, ref context);
            },
            volume =>
            {
                var context = Guid.Empty;
                volume.SetMasterVolume((float)value, ref context);
            });

        UpdateLocal(sliderId, slider => slider.Value = Math.Round(Math.Clamp(value, 0.0, 1.0), 2));
        return Task.CompletedTask;
    }

    public Task SetMutedAsync(string sliderId, bool muted, CancellationToken cancellationToken = default)
    {
        Apply(sliderId,
            endpoint =>
            {
                var context = Guid.Empty;
                endpoint.SetMute(muted, ref context);
            },
            volume =>
            {
                var context = Guid.Empty;
                volume.SetMute(muted, ref context);
            });

        UpdateLocal(sliderId, slider => slider.Muted = muted);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Routes a write to either an endpoint volume or an application session volume,
    /// depending on which kind of slider the id names.
    /// </summary>
    private void Apply(string sliderId,
        Action<CoreAudio.IAudioEndpointVolume> onEndpoint,
        Action<CoreAudio.ISimpleAudioVolume> onSession)
    {
        CoreAudio.IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumerator();

            if (sliderId is RenderSliderId or CaptureSliderId)
            {
                var flow = sliderId == RenderSliderId ? CoreAudio.EDataFlow.Render : CoreAudio.EDataFlow.Capture;
                ApplyToDevice(enumerator, flow, onEndpoint);
                return;
            }

            if (sliderId.StartsWith(AppPrefix, StringComparison.Ordinal))
            {
                ApplyToSessions(enumerator, sliderId[AppPrefix.Length..], onSession);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Applying a Windows audio change failed for {SliderId}", sliderId);
        }
        finally
        {
            Release(enumerator);
        }
    }

    private static void ApplyToDevice(CoreAudio.IMMDeviceEnumerator enumerator,
        CoreAudio.EDataFlow flow,
        Action<CoreAudio.IAudioEndpointVolume> action)
    {
        CoreAudio.IMMDevice? device = null;
        object? volumeObject = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(flow, CoreAudio.ERole.Multimedia, out device) != 0 ||
                device is null)
            {
                return;
            }

            var iid = CoreAudio.IID_IAudioEndpointVolume;
            if (device.Activate(ref iid, CoreAudio.ClsCtxAll, IntPtr.Zero, out volumeObject) != 0)
            {
                return;
            }

            action((CoreAudio.IAudioEndpointVolume)volumeObject);
        }
        finally
        {
            Release(volumeObject);
            Release(device);
        }
    }

    /// <summary>
    /// Applies the change to every session of the process, so an app with several
    /// streams moves as one.
    /// </summary>
    private static void ApplyToSessions(CoreAudio.IMMDeviceEnumerator enumerator,
        string processName,
        Action<CoreAudio.ISimpleAudioVolume> action)
    {
        CoreAudio.IMMDevice? device = null;
        object? managerObject = null;
        CoreAudio.IAudioSessionEnumerator? sessions = null;

        try
        {
            if (enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Render,
                    CoreAudio.ERole.Multimedia,
                    out device) != 0 ||
                device is null)
            {
                return;
            }

            var iid = CoreAudio.IID_IAudioSessionManager2;
            if (device.Activate(ref iid, CoreAudio.ClsCtxAll, IntPtr.Zero, out managerObject) != 0)
            {
                return;
            }

            var manager = (CoreAudio.IAudioSessionManager2)managerObject;
            if (manager.GetSessionEnumerator(out sessions) != 0 || sessions is null)
            {
                return;
            }

            if (sessions.GetCount(out var count) != 0)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                CoreAudio.IAudioSessionControl? control = null;
                try
                {
                    if (sessions.GetSession(i, out control) != 0 || control is null)
                    {
                        continue;
                    }

                    if (control is not CoreAudio.IAudioSessionControl2 control2)
                    {
                        continue;
                    }

                    if (control2.GetProcessId(out var processId) != 0 || processId <= 0)
                    {
                        continue;
                    }

                    var name = GetProcessName(processId);
                    if (name is null ||
                        !name.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (control is CoreAudio.ISimpleAudioVolume volume)
                    {
                        action(volume);
                    }
                }
                catch
                {
                    // Session went away mid-write
                }
                finally
                {
                    Release(control);
                }
            }
        }
        finally
        {
            Release(sessions);
            Release(managerObject);
            Release(device);
        }
    }

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

    public MacroDeckSlider? GetSlider(string sliderId)
    {
        lock (_stateLock)
        {
            return _sliders.TryGetValue(sliderId, out var slider) ? slider.Clone() : null;
        }
    }

    private void SetAvailable(bool available)
    {
        if (_available == available)
        {
            return;
        }

        _available = available;
        Logger.Information("Windows audio is {State}", available ? "available" : "unavailable");
    }

    /// <summary>
    /// COM objects handed out by Core Audio are not tracked by the GC's finalizer in a
    /// timely way; releasing them explicitly keeps the poll loop from leaking handles.
    /// </summary>
    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch
            {
                // Already released
            }
        }
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
