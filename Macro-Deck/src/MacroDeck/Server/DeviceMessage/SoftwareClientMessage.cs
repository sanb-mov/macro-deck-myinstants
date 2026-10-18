using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using SuchByte.MacroDeck.Device;
using Serilog;
using SuchByte.MacroDeck.Icons;
using SuchByte.MacroDeck.JSON;
using SuchByte.MacroDeck.Sliders;

namespace SuchByte.MacroDeck.Server.DeviceMessage;

public class SoftwareClientMessage : IDeviceMessage
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SoftwareClientMessage));

    public void Connected(MacroDeckClient macroDeckClient)
    {
        SendConfiguration(macroDeckClient);
        SendAllButtons(macroDeckClient);
        if (macroDeckClient.DeviceType != DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).DeviceType)
        {
            DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).DeviceType = macroDeckClient.DeviceType;
            DeviceManager.SaveKnownDevices();
        }
    }

    public void SendAllButtons(MacroDeckClient macroDeckClient)
    {
        if (macroDeckClient == null || macroDeckClient.Folder == null || macroDeckClient.Folder.ActionButtons == null)
        {
            return;
        }

        var buttons = new ConcurrentBag<JObject>();

        Parallel.ForEach(macroDeckClient.Folder.ActionButtons,
            actionButton =>
            {
                var IconBase64 = "";
                var LabelBase64 = "";
                string BackgroundColorHex;
                if (!actionButton.State)
                {
                    if (!string.IsNullOrWhiteSpace(actionButton.IconOff))
                    {
                        var icon = IconManager.GetIconByString(actionButton.IconOff);
                        if (icon != null)
                        {
                            IconBase64 = icon.IconBase64;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(actionButton.LabelOff.LabelText))
                    {
                        LabelBase64 = actionButton.LabelOff.LabelBase64 ?? "";
                    }

                    BackgroundColorHex
                        = $"#{actionButton.BackColorOff.R:X2}{actionButton.BackColorOff.G:X2}{actionButton.BackColorOff.B:X2}";
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(actionButton.IconOn))
                    {
                        var icon = IconManager.GetIconByString(actionButton.IconOn);
                        if (icon != null)
                        {
                            IconBase64 = icon.IconBase64;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(actionButton.LabelOn.LabelText))
                    {
                        LabelBase64 = actionButton.LabelOn.LabelBase64 ?? "";
                    }

                    BackgroundColorHex
                        = $"#{actionButton.BackColorOn.R:X2}{actionButton.BackColorOn.G:X2}{actionButton.BackColorOn.B:X2}";
                }

                var actionButtonObject = JObject.FromObject(new
                {
                    IconBase64,
                    actionButton.Position_X,
                    actionButton.Position_Y,
                    LabelBase64,
                    BackgroundColorHex
                });
                buttons.Add(actionButtonObject);
            });

        var buttonsObject = JObject.FromObject(new
        {
            Method = JsonMethod.GET_BUTTONS.ToString(),
            Buttons = buttons
        });

        MacroDeckServer.Send(macroDeckClient, buttonsObject);
    }

    public void SendConfiguration(MacroDeckClient macroDeckClient)
    {
        if (macroDeckClient == null)
        {
            return;
        }

        var configurationObject = JObject.FromObject(new
        {
            Method = JsonMethod.GET_CONFIG.ToString(),
            macroDeckClient.Profile.Rows,
            macroDeckClient.Profile.Columns,
            macroDeckClient.Profile.ButtonSpacing,
            macroDeckClient.Profile.ButtonRadius,
            macroDeckClient.Profile.ButtonBackground,
            DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).Configuration.Brightness,
            DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).Configuration.AutoConnect,
            WakeLock = Enum.GetName(typeof(WakeLockMethod),
                DeviceManager.GetMacroDeckDevice(macroDeckClient.ClientId).Configuration.WakeLockMethod),
            SupportButtonReleaseLongPress = true,
            SupportSliders = true
        });
        Logger.Debug("{Configuration}", configurationObject.ToString());
        MacroDeckServer.Send(macroDeckClient, configurationObject);
    }

    public void UpdateButton(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton)
    {
        if (macroDeckClient.Folder == null || !macroDeckClient.Folder.ActionButtons.Contains(actionButton))
        {
            return;
        }

        var IconBase64 = "";
        var LabelBase64 = "";
        string BackgroundColorHex;
        if (!actionButton.State)
        {
            if (!string.IsNullOrWhiteSpace(actionButton.IconOff))
            {
                var icon = IconManager.GetIconByString(actionButton.IconOff);
                if (icon != null)
                {
                    IconBase64 = icon.IconBase64;
                }
            }

            if (!string.IsNullOrWhiteSpace(actionButton.LabelOff.LabelText))
            {
                LabelBase64 = actionButton.LabelOff.LabelBase64 ?? "";
            }

            BackgroundColorHex
                = $"#{actionButton.BackColorOff.R:X2}{actionButton.BackColorOff.G:X2}{actionButton.BackColorOff.B:X2}";
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(actionButton.IconOn))
            {
                var icon = IconManager.GetIconByString(actionButton.IconOn);
                if (icon != null)
                {
                    IconBase64 = icon.IconBase64;
                }
            }

            if (!string.IsNullOrWhiteSpace(actionButton.LabelOn.LabelText))
            {
                LabelBase64 = actionButton.LabelOn.LabelBase64 ?? "";
            }

            BackgroundColorHex
                = $"#{actionButton.BackColorOn.R:X2}{actionButton.BackColorOn.G:X2}{actionButton.BackColorOn.B:X2}";
        }

        var actionButtonObject = JObject.FromObject(new
        {
            IconBase64,
            actionButton.Position_X,
            actionButton.Position_Y,
            LabelBase64,
            BackgroundColorHex
        });

        var updateObject = JObject.FromObject(new
        {
            Method = JsonMethod.UPDATE_BUTTON.ToString(),
            Buttons = new List<JObject>
            {
                actionButtonObject
            }
        });

        MacroDeckServer.Send(macroDeckClient, updateObject);
    }

    public void SendAllSliders(MacroDeckClient macroDeckClient, IReadOnlyList<MacroDeckSlider> sliders)
    {
        if (macroDeckClient is null)
        {
            return;
        }

        var ordered = sliders.OrderBy(x => x.Position).ToList();
        var (rows, columns) = GetGridSize(macroDeckClient);
        var layout = BuildSliderLayout(ordered, rows, columns);
        var style = (int)(macroDeckClient.Folder?.SliderStyle ?? SliderStyle.Classic);

        var slidersObject = JObject.FromObject(new
        {
            Method = JsonMethod.GET_SLIDERS.ToString(),
            Style = style,
            CanGoBack = MacroDeckServer.CanGoBack(macroDeckClient),
            Sliders = ordered.Select(slider => CreateSliderObject(slider, layout, style)).ToList(),
            Groups = layout.Labels
                .Select(x => JObject.FromObject(new
                {
                    x.Name,
                    x.Row,
                    x.Column,
                    x.RowSpan
                }))
                .ToList()
        });

        MacroDeckServer.Send(macroDeckClient, slidersObject);
    }

    public void UpdateSlider(MacroDeckClient macroDeckClient, MacroDeckSlider slider)
    {
        if (macroDeckClient is null)
        {
            return;
        }

        // The client addresses widgets by their grid cell, so a single update has to be
        // placed on the same cell the slider got in the last GET_SLIDERS.
        var ordered = SliderManager.GetCachedSliders(slider.ProviderId)
            .OrderBy(x => x.Position)
            .ToList();
        if (!ordered.Any(x => x.SliderId == slider.SliderId))
        {
            return;
        }

        var (rows, columns) = GetGridSize(macroDeckClient);
        var layout = BuildSliderLayout(ordered, rows, columns);
        var style = (int)(macroDeckClient.Folder?.SliderStyle ?? SliderStyle.Classic);

        var updateObject = JObject.FromObject(new
        {
            Method = JsonMethod.UPDATE_SLIDER.ToString(),
            Style = style,
            Sliders = new List<JObject>
            {
                CreateSliderObject(slider, layout, style)
            }
        });

        MacroDeckServer.Send(macroDeckClient, updateObject);
    }

    private static (int Rows, int Columns) GetGridSize(MacroDeckClient macroDeckClient)
    {
        var rows = macroDeckClient.Profile?.Rows ?? 3;
        var columns = macroDeckClient.Profile?.Columns ?? 5;
        return (Math.Max(rows, 1), Math.Max(columns, 1));
    }

    /// <summary>
    /// Places the sliders on the deck grid: one column per slider, spanning the full
    /// height of its band. Sliders wrap to a new band when they run out of columns,
    /// and a new group (e.g. Sonar's Streaming and Monitoring) always starts its own
    /// band so the two mixes never share a row.
    /// </summary>
    /// <summary>
    /// Places the sliders on the deck grid: one column per slider, spanning the full
    /// height of its band. Sliders wrap to a new band when they run out of columns,
    /// and a new group (e.g. Sonar's Streaming and Monitoring) always starts its own
    /// band so the two mixes never share a row.
    ///
    /// When the sliders carry group names, the first column of every band is reserved
    /// for the group label so the client can show which mix a row belongs to.
    /// </summary>
    private static SliderLayout BuildSliderLayout(IReadOnlyList<MacroDeckSlider> ordered,
        int rows,
        int columns)
    {
        var groups = ordered
            .GroupBy(x => x.Group ?? string.Empty)
            .Select(x => new { Name = x.Key, Sliders = x.ToList() })
            .ToList();

        // Only spend a column on labels when there is actually something to tell apart
        // and the deck is wide enough to afford it.
        var showLabels = columns >= 3 &&
            groups.Count > 1 &&
            groups.All(x => !string.IsNullOrWhiteSpace(x.Name));

        var firstColumn = showLabels ? 1 : 0;
        var capacity = Math.Max(1, columns - firstColumn);

        var bandCount = groups.Sum(x => Math.Max(1, (int)Math.Ceiling(x.Sliders.Count / (double)capacity)));
        var rowSpan = Math.Max(1, rows / Math.Max(bandCount, 1));

        var cells = new Dictionary<string, SliderCell>();
        var labels = new List<SliderGroupLabel>();
        var band = 0;

        foreach (var group in groups)
        {
            if (showLabels)
            {
                labels.Add(new SliderGroupLabel(group.Name, band * rowSpan, 0, rowSpan));
            }

            var column = firstColumn;
            foreach (var slider in group.Sliders)
            {
                if (column == columns)
                {
                    column = firstColumn;
                    band++;
                }

                cells[slider.SliderId] = new SliderCell(band * rowSpan, column, rowSpan);
                column++;
            }

            band++;
        }

        return new SliderLayout(cells, labels);
    }

    private sealed record SliderLayout(Dictionary<string, SliderCell> Cells, List<SliderGroupLabel> Labels);

    private readonly record struct SliderGroupLabel(string Name, int Row, int Column, int RowSpan);

    private readonly record struct SliderCell(int Row, int Column, int RowSpan);

    private static JObject CreateSliderObject(MacroDeckSlider slider, SliderLayout layout, int style)
    {
        var cell = layout.Cells.TryGetValue(slider.SliderId, out var found)
            ? found
            : new SliderCell(0, 0, 1);

        return JObject.FromObject(new
        {
            slider.SliderId,
            slider.ProviderId,
            slider.DisplayName,
            slider.Position,
            slider.Value,
            slider.Muted,
            slider.SupportsMute,
            slider.ColorHex,
            IconBase64 = slider.IconBase64 ?? "",
            Group = slider.Group ?? "",
            Row = cell.Row,
            Column = cell.Column,
            RowSpan = cell.RowSpan,
            ColSpan = 1,
            Style = style
        });
    }
}
