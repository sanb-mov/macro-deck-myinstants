using SuchByte.MacroDeck.GUI.CustomControls.Sliders;
using SuchByte.MacroDeck.Sliders;

namespace SuchByte.MacroDeck.GUI.MainWindowViews;

/// <summary>
/// Main window view that shows the sliders of a slider provider (e.g. SteelSeries Sonar)
/// and lets the user control them with the mouse.
/// </summary>
public class SlidersView : UserControl
{
    private readonly FlowLayoutPanel _groupsPanel;
    private readonly Label _statusLabel;
    private readonly System.Windows.Forms.ComboBox _providerComboBox;
    private readonly Dictionary<string, SliderControl> _sliderControls = new();

    private readonly System.Windows.Forms.ComboBox _styleComboBox;

    private string? _providerId;
    private SliderStyle _style;
    private bool _loading;

    /// <summary>
    /// Provider whose sliders are currently shown
    /// </summary>
    public string? ProviderId => _providerId;

    /// <summary>
    /// Style the sliders are currently drawn with
    /// </summary>
    public SliderStyle SliderStyle => _style;

    public SlidersView(string? providerId = null, SliderStyle style = SliderStyle.Classic)
    {
        _providerId = providerId;
        _style = style;

        Dock = DockStyle.Fill;
        BackColor = Colors.Background;
        Name = "Sliders";

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = Colors.Background
        };

        var title = new Label
        {
            Text = "Sliders",
            Font = new Font("Tahoma", 15F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(16, 14)
        };
        header.Controls.Add(title);

        _providerComboBox = new System.Windows.Forms.ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Colors.Surface2,
            ForeColor = Color.White,
            Font = new Font("Tahoma", 10F),
            Location = new Point(140, 16),
            Size = new Size(260, 28)
        };
        _providerComboBox.SelectedIndexChanged += ProviderComboBoxOnSelectedIndexChanged;
        header.Controls.Add(_providerComboBox);

        _styleComboBox = new System.Windows.Forms.ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Colors.Surface2,
            ForeColor = Color.White,
            Font = new Font("Tahoma", 10F),
            Location = new Point(410, 16),
            Size = new Size(140, 28)
        };
        _styleComboBox.Items.AddRange(["Classic", "Capsule", "Knob"]);
        _styleComboBox.SelectedIndex = (int)_style;
        _styleComboBox.SelectedIndexChanged += StyleComboBoxOnSelectedIndexChanged;
        header.Controls.Add(_styleComboBox);

        _statusLabel = new Label
        {
            Text = "",
            Font = new Font("Tahoma", 10F),
            ForeColor = Color.FromArgb(200, 160, 60),
            AutoSize = true,
            Location = new Point(566, 22)
        };
        header.Controls.Add(_statusLabel);

        _groupsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoScroll = true,
            WrapContents = false,
            Padding = new Padding(12),
            BackColor = Colors.Background
        };

        Controls.Add(_groupsPanel);
        Controls.Add(header);

        SliderManager.OnProvidersChanged += OnProvidersChanged;
        SliderManager.OnSliderChanged += OnSliderChanged;
        SliderManager.OnSlidersInvalidated += OnSlidersInvalidated;

        LoadProviders();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SliderManager.OnProvidersChanged -= OnProvidersChanged;
            SliderManager.OnSliderChanged -= OnSliderChanged;
            SliderManager.OnSlidersInvalidated -= OnSlidersInvalidated;
        }

        base.Dispose(disposing);
    }

    private void LoadProviders()
    {
        _loading = true;
        try
        {
            var providers = SliderManager.Providers.ToList();

            _providerComboBox.Items.Clear();
            foreach (var provider in providers)
            {
                _providerComboBox.Items.Add(provider.DisplayName);
            }

            _providerComboBox.Visible = providers.Count > 1;

            if (providers.Count == 0)
            {
                _providerId = null;
                _statusLabel.Text = "No slider provider is installed";
                _groupsPanel.Controls.Clear();
                _sliderControls.Clear();
                return;
            }

            var index = _providerId is null
                ? 0
                : Math.Max(providers.FindIndex(x => x.ProviderId == _providerId), 0);

            _providerId = providers[index].ProviderId;
            _providerComboBox.SelectedIndex = index;
        }
        finally
        {
            _loading = false;
        }

        if (IsHandleCreated)
        {
            _ = ReloadSlidersAsync();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _ = ReloadSlidersAsync();
    }

    private void ProviderComboBoxOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loading || _providerComboBox.SelectedIndex < 0)
        {
            return;
        }

        var providers = SliderManager.Providers.ToList();
        if (_providerComboBox.SelectedIndex >= providers.Count)
        {
            return;
        }

        _providerId = providers[_providerComboBox.SelectedIndex].ProviderId;
        _ = ReloadSlidersAsync();
    }

    /// <summary>
    /// Fetches the current sliders and rebuilds the whole view
    /// </summary>
    private async Task ReloadSlidersAsync()
    {
        var providerId = _providerId;
        var sliders = await SliderManager.GetSlidersAsync(providerId);

        if (IsDisposed || providerId != _providerId)
        {
            return;
        }

        RunOnUiThread(() => BuildSliders(sliders));
    }

    private void BuildSliders(IReadOnlyList<MacroDeckSlider> sliders)
    {
        _groupsPanel.SuspendLayout();
        _groupsPanel.Controls.Clear();
        _sliderControls.Clear();

        var provider = SliderManager.GetProvider(_providerId);
        _statusLabel.Text = provider is null
            ? "No slider provider is installed"
            : provider.IsAvailable
                ? ""
                : $"{provider.DisplayName} is not running";

        foreach (var group in sliders.GroupBy(x => x.Group ?? ""))
        {
            if (!string.IsNullOrWhiteSpace(group.Key))
            {
                _groupsPanel.Controls.Add(new Label
                {
                    Text = group.Key,
                    Font = new Font("Tahoma", 11F, FontStyle.Bold),
                    ForeColor = Color.White,
                    AutoSize = true,
                    Margin = new Padding(4, 8, 4, 4)
                });
            }

            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                BackColor = Colors.Background,
                Margin = new Padding(0, 0, 0, 12)
            };

            foreach (var slider in group.OrderBy(x => x.Position))
            {
                var control = new SliderControl(slider.Clone(), _style)
                {
                    Margin = new Padding(6)
                };
                control.ValueChanged += SliderControlOnValueChanged;
                control.MuteToggled += SliderControlOnMuteToggled;

                _sliderControls[slider.SliderId] = control;
                row.Controls.Add(control);
            }

            _groupsPanel.Controls.Add(row);
        }

        _groupsPanel.ResumeLayout();
    }

    private void SliderControlOnValueChanged(object? sender, double value)
    {
        if (sender is not SliderControl control || _providerId is null)
        {
            return;
        }

        var providerId = _providerId;
        _ = Task.Run(async () => await SliderManager.SetValueAsync(providerId, control.SliderId, value));
    }

    private void SliderControlOnMuteToggled(object? sender, bool muted)
    {
        if (sender is not SliderControl control || _providerId is null)
        {
            return;
        }

        var providerId = _providerId;
        _ = Task.Run(async () => await SliderManager.SetMutedAsync(providerId, control.SliderId, muted));
    }

    private void StyleComboBoxOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_styleComboBox.SelectedIndex < 0)
        {
            return;
        }

        _style = (SliderStyle)_styleComboBox.SelectedIndex;
        foreach (var control in _sliderControls.Values)
        {
            control.Style = _style;
        }
    }

    private void OnProvidersChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(LoadProviders);
    }

    private void OnSliderChanged(object? sender, MacroDeckSlider slider)
    {
        if (slider.ProviderId != _providerId)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            if (_sliderControls.TryGetValue(slider.SliderId, out var control))
            {
                control.UpdateSlider(slider);
            }
        });
    }

    private void OnSlidersInvalidated(object? sender, string providerId)
    {
        if (providerId != _providerId)
        {
            return;
        }

        _ = ReloadSlidersAsync();
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // The view was closed while an update was in flight
        }
    }
}
