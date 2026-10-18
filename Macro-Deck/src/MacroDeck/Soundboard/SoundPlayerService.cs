using System.Windows.Threading;
using Serilog;

namespace SuchByte.MacroDeck.Soundboard;

/// <summary>
/// Plays the sounds of myinstants.com on this PC. A dedicated STA thread with a dispatcher
/// owns the WPF <see cref="System.Windows.Media.MediaPlayer"/>.
/// </summary>
public static class SoundPlayerService
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SoundPlayerService));
    private static readonly Lazy<Dispatcher> PlayerDispatcher = new(StartDispatcherThread);

    private static System.Windows.Media.MediaPlayer? _player;

    public static void Play(SoundItem sound)
    {
        // Only play files hosted by MyInstants, never an arbitrary URL
        if (!Uri.TryCreate(sound.Url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, MyInstantsService.Host, StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith("/media/sounds/", StringComparison.Ordinal))
        {
            Logger.Warning("Refused to play a sound from {Url}", sound.Url);
            return;
        }

        PlayerDispatcher.Value.BeginInvoke(() =>
        {
            try
            {
                _player?.Close();
                _player = new System.Windows.Media.MediaPlayer();
                _player.MediaFailed += (_, e) => Logger.Warning(e.ErrorException, "Could not play {Url}", uri);
                _player.Open(uri);
                _player.Play();
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Could not play {Url}", uri);
            }
        });
    }

    public static void Stop()
    {
        if (!PlayerDispatcher.IsValueCreated)
        {
            return;
        }

        PlayerDispatcher.Value.BeginInvoke(() => _player?.Stop());
    }

    private static Dispatcher StartDispatcherThread()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Soundboard player"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        return dispatcher!;
    }
}
