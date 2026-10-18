using System.Drawing.Imaging;
using Newtonsoft.Json.Linq;
using Serilog;
using SuchByte.MacroDeck.ActionButton;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Soundboard;

/// <summary>
/// Serves a soundboard folder to the clients: the trending sounds of myinstants.com as ordinary deck buttons,
/// plus a few navigation buttons (back to the parent folder, previous / next page, stop).
/// The buttons are made up on the fly, so a soundboard folder has no <see cref="ActionButton.ActionButton"/>s.
/// </summary>
public static class SoundboardFolderService
{
    internal enum Nav
    {
        Back,
        Previous,
        Next,
        Stop,
        Page
    }

    internal sealed record Layout(
        int Rows,
        int Columns,
        Dictionary<(int Row, int Column), Nav> NavCells,
        List<(int Row, int Column)> SoundCells);

    /// <summary>
    /// What a client currently sees, so a press can be mapped back to a sound
    /// </summary>
    internal sealed record State(Layout? Layout, IReadOnlyList<SoundItem> Sounds, bool HasMore, string Key);

    private const int RefreshSeconds = 120;

    private static readonly ILogger Logger = Log.ForContext(typeof(SoundboardFolderService));
    private static readonly Dictionary<string, string> LabelCache = new();
    private static readonly Lock LabelLock = new();
    private static System.Threading.Timer? _refreshTimer;

    private static readonly string Gray = "#333333";
    private static readonly string Dim = "#262626";
    private static readonly string Blue = "#0067CD";
    private static readonly string Red = "#BE3232";
    private static readonly string Amber = "#B26B00";

    /// <summary>
    /// Sends the current page of the folder to the client as GET_BUTTONS
    /// </summary>
    /// <param name="onlyIfChanged">Skip the message when the client already shows exactly this page</param>
    internal static async Task SendAsync(MacroDeckClient client, bool forceRefresh = false, bool onlyIfChanged = false)
    {
        try
        {
            if (client.Folder is not { IsSoundboardFolder: true } || client.DeviceMessage is null)
            {
                return;
            }

            EnsureAutoRefresh();

            if (client.SupportsSoundboardView)
            {
                await SendViewAsync(client, forceRefresh, onlyIfChanged);
                return;
            }

            var layout = BuildLayout(client.Profile?.Rows ?? 3, client.Profile?.Columns ?? 5);
            var page = Math.Max(client.SoundboardPage, 1);
            var result = await MyInstantsService.GetSoundPageAsync(null, page, layout.SoundCells.Count, forceRefresh);

            var previous = client.Soundboard;
            var key = $"{layout.Rows}x{layout.Columns}";
            if (onlyIfChanged && previous is not null && previous.Key == key &&
                previous.Sounds.Select(x => x.Id).SequenceEqual(result.Sounds.Select(x => x.Id)))
            {
                return;
            }

            // The client may have left the folder while the sounds were loading
            if (client.Folder is not { IsSoundboardFolder: true } || page != Math.Max(client.SoundboardPage, 1))
            {
                return;
            }

            client.Soundboard = new State(layout, result.Sounds, result.HasMore, key);

            var buttons = new List<JObject>();
            foreach (var (cell, nav) in layout.NavCells)
            {
                var enabled = nav switch
                {
                    Nav.Previous => page > 1,
                    Nav.Next => result.HasMore,
                    _ => true
                };

                var (text, color) = nav switch
                {
                    Nav.Back => ("< BACK", Amber),
                    Nav.Previous => ("< PREV", enabled ? Blue : Dim),
                    Nav.Next => ("NEXT >", enabled ? Blue : Dim),
                    Nav.Stop => ("STOP", Red),
                    _ => ($"Page {page}", Dim)
                };
                buttons.Add(CreateButton(cell.Row, cell.Column, text, color));
            }

            for (var i = 0; i < result.Sounds.Count && i < layout.SoundCells.Count; i++)
            {
                var cell = layout.SoundCells[i];
                buttons.Add(CreateButton(cell.Row, cell.Column, result.Sounds[i].Name, Gray));
            }

            MacroDeckServer.Send(client, JObject.FromObject(new
            {
                Method = JsonMethod.GET_BUTTONS.ToString(),
                Buttons = buttons
            }));
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Sending the soundboard folder caused an exception");
        }
    }

    /// <summary>
    /// Sends the current page to a client that draws the soundboard itself: the names of the sounds and the
    /// size of the grid, which is the one of the soundboard settings
    /// </summary>
    private static async Task SendViewAsync(MacroDeckClient client, bool forceRefresh, bool onlyIfChanged)
    {
        var page = Math.Max(client.SoundboardPage, 1);
        var rows = SoundboardSettings.Rows;
        var columns = SoundboardSettings.Columns;
        var query = client.SoundboardQuery;
        var result = await MyInstantsService.GetSoundPageAsync(query, page, rows * columns, forceRefresh);

        var previous = client.Soundboard;
        var key = $"{rows}x{columns}|{query}";
        if (onlyIfChanged && previous is not null && previous.Key == key &&
            previous.Sounds.Select(x => x.Id).SequenceEqual(result.Sounds.Select(x => x.Id)))
        {
            return;
        }

        // The client may have left the folder while the sounds were loading
        if (client.Folder is not { IsSoundboardFolder: true } || page != Math.Max(client.SoundboardPage, 1))
        {
            return;
        }

        client.Soundboard = new State(null, result.Sounds, result.HasMore, key);

        MacroDeckServer.Send(client, JObject.FromObject(new
        {
            Method = JsonMethod.GET_SOUNDBOARD.ToString(),
            Page = page,
            Query = query,
            result.HasMore,
            Rows = rows,
            Columns = columns,
            Sounds = result.Sounds.Select(x => new { x.Name }).ToList()
        }));
    }

    /// <summary>
    /// Handles what the user did in the soundboard view of a client
    /// </summary>
    internal static void HandleAction(MacroDeckClient client, string? action, int index, string? query = null)
    {
        if (client.Folder is not { IsSoundboardFolder: true })
        {
            return;
        }

        var state = client.Soundboard;
        switch (action)
        {
            case "PLAY" when state is not null && index >= 0 && index < state.Sounds.Count:
                SoundPlayerService.Play(state.Sounds[index]);
                break;
            case "STOP":
                SoundPlayerService.Stop();
                break;
            case "SEARCH":
                // An empty query goes back to the trending sounds
                client.SoundboardQuery = (query ?? "").Trim();
                client.SoundboardPage = 1;
                _ = SendAsync(client);
                break;
            case "BACK":
                MacroDeckServer.GoToParentFolder(client);
                break;
            case "PREVIOUS" when client.SoundboardPage > 1:
                client.SoundboardPage--;
                _ = SendAsync(client);
                break;
            case "NEXT" when state?.HasMore == true:
                client.SoundboardPage++;
                _ = SendAsync(client);
                break;
            case "ROWS_UP":
            case "ROWS_DOWN":
            case "COLUMNS_UP":
            case "COLUMNS_DOWN":
                ChangeGrid(action);
                break;
        }
    }

    /// <summary>
    /// Changes the page size of the soundboard settings and re-flows every client that shows a soundboard
    /// </summary>
    private static void ChangeGrid(string action)
    {
        var rows = SoundboardSettings.Rows + (action == "ROWS_UP" ? 1 : action == "ROWS_DOWN" ? -1 : 0);
        var columns = SoundboardSettings.Columns + (action == "COLUMNS_UP" ? 1 : action == "COLUMNS_DOWN" ? -1 : 0);
        SoundboardSettings.Set(rows, columns);

        foreach (var other in MacroDeckServer.Clients.ToList())
        {
            if (other.Folder is { IsSoundboardFolder: true } && other.SupportsSoundboardView)
            {
                other.SoundboardPage = 1;
                _ = SendAsync(other);
            }
        }
    }

    /// <summary>
    /// Handles a short press on a button of a soundboard folder
    /// </summary>
    internal static void HandlePress(MacroDeckClient client, int row, int column)
    {
        var state = client.Soundboard;
        if (state?.Layout is null)
        {
            return;
        }

        if (state.Layout.NavCells.TryGetValue((row, column), out var nav))
        {
            switch (nav)
            {
                case Nav.Back:
                    MacroDeckServer.GoToParentFolder(client);
                    break;
                case Nav.Previous when client.SoundboardPage > 1:
                    client.SoundboardPage--;
                    _ = SendAsync(client);
                    break;
                case Nav.Next when state.HasMore:
                    client.SoundboardPage++;
                    _ = SendAsync(client);
                    break;
                case Nav.Stop:
                    SoundPlayerService.Stop();
                    break;
            }

            return;
        }

        var index = state.Layout.SoundCells.IndexOf((row, column));
        if (index >= 0 && index < state.Sounds.Count)
        {
            SoundPlayerService.Play(state.Sounds[index]);
        }
    }

    /// <summary>
    /// The last row holds the navigation buttons, every other cell is a sound. A grid too small for all the
    /// navigation buttons keeps the most important ones first.
    /// </summary>
    private static Layout BuildLayout(int rows, int columns)
    {
        rows = Math.Max(rows, 1);
        columns = Math.Max(columns, 1);

        var order = new List<Nav> { Nav.Back, Nav.Previous, Nav.Next, Nav.Stop, Nav.Page };
        order = order.Take(columns).ToList();

        // Leave room for at least one sound
        while (order.Count > 1 && rows * columns - order.Count < 1)
        {
            order.RemoveAt(order.Count - 1);
        }

        var navCells = new Dictionary<(int Row, int Column), Nav>();
        for (var i = 0; i < order.Count; i++)
        {
            navCells[(rows - 1, i)] = order[i];
        }

        var soundCells = new List<(int Row, int Column)>();
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (!navCells.ContainsKey((row, column)))
                {
                    soundCells.Add((row, column));
                }
            }
        }

        return new Layout(rows, columns, navCells, soundCells);
    }

    private static JObject CreateButton(int row, int column, string text, string colorHex)
    {
        return JObject.FromObject(new
        {
            IconBase64 = "",
            Position_X = column,
            Position_Y = row,
            LabelBase64 = GetLabel(text),
            BackgroundColorHex = colorHex
        });
    }

    /// <summary>
    /// Renders the text like a button label of the editor: a 250x250 image with an outlined text in the middle
    /// </summary>
    private static string GetLabel(string text)
    {
        if (text.Length > 36)
        {
            text = text[..35] + "…";
        }

        lock (LabelLock)
        {
            if (LabelCache.TryGetValue(text, out var cached))
            {
                return cached;
            }
        }

        // Long names get a smaller font so they still fit on the button
        var size = text.Length switch
        {
            <= 8 => 14F,
            <= 16 => 11F,
            <= 28 => 9F,
            _ => 7.5F
        };

        using var bitmap = new Bitmap(250, 250, PixelFormat.Format32bppArgb);
        LabelGenerator.GetLabel(bitmap,
            text,
            ButtonLabelPosition.CENTER,
            new Font("Tahoma", size, FontStyle.Bold),
            Color.White,
            Color.Black,
            new SizeF(2.0F, 2.0F));
        var label = Base64.GetBase64FromImage(bitmap);

        lock (LabelLock)
        {
            if (LabelCache.Count > 1000)
            {
                LabelCache.Clear();
            }

            LabelCache[text] = label;
        }

        return label;
    }

    /// <summary>
    /// Checks every couple of minutes whether new sounds were added to the site and pushes them to the clients
    /// that sit in a soundboard folder
    /// </summary>
    private static void EnsureAutoRefresh()
    {
        if (_refreshTimer is not null)
        {
            return;
        }

        lock (LabelLock)
        {
            _refreshTimer ??= new System.Threading.Timer(_ =>
            {
                foreach (var client in MacroDeckServer.Clients.ToList())
                {
                    if (client.Folder is { IsSoundboardFolder: true })
                    {
                        _ = SendAsync(client, true, true);
                    }
                }
            }, null, TimeSpan.FromSeconds(RefreshSeconds), TimeSpan.FromSeconds(RefreshSeconds));
        }
    }
}
