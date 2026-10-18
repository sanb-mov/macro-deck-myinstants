using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Actions;
using SuchByte.MacroDeck.InternalPlugins.SonarPlugin.Api;
using SuchByte.MacroDeck.Plugins;

namespace SuchByte.MacroDeck.InternalPlugins.SonarPlugin.GUI;

public enum SonarActionConfiguratorMode
{
    SetVolume,
    AdjustVolume,
    ToggleMute,
    SwitchMode
}

/// <summary>
/// Shared configuration UI for every Sonar action. Which inputs are shown depends on
/// the <see cref="SonarActionConfiguratorMode"/> the action asks for.
/// </summary>
public class SonarActionConfigurator : ActionConfigControl
{
    private readonly PluginAction _action;
    private readonly SonarActionConfiguratorMode _mode;
    private readonly SonarActionConfiguration _configuration;

    private readonly System.Windows.Forms.ComboBox _channelComboBox = new();
    private readonly System.Windows.Forms.ComboBox _modeComboBox = new();
    private readonly NumericUpDown _valueUpDown = new();
    private readonly Label _valueLabel = new();

    public SonarActionConfigurator(PluginAction action, SonarActionConfiguratorMode mode)
    {
        _action = action;
        _mode = mode;
        _configuration = SonarActionConfiguration.Deserialize(action.Configuration);

        BuildLayout();
        LoadConfiguration();
    }

    private void BuildLayout()
    {
        SuspendLayout();

        var font = new Font("Tahoma", 10F);
        var y = 20;

        if (_mode == SonarActionConfiguratorMode.SwitchMode)
        {
            Controls.Add(CreateLabel("Mode", font, y));
            y += 30;

            _modeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _modeComboBox.FlatStyle = FlatStyle.Flat;
            _modeComboBox.BackColor = Color.FromArgb(65, 65, 65);
            _modeComboBox.ForeColor = Color.White;
            _modeComboBox.Font = font;
            _modeComboBox.Location = new Point(20, y);
            _modeComboBox.Size = new Size(420, 28);
            _modeComboBox.Items.AddRange(["Classic", "Streamer"]);
            Controls.Add(_modeComboBox);

            ResumeLayout(false);
            return;
        }

        Controls.Add(CreateLabel("Channel", font, y));
        y += 30;

        _channelComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _channelComboBox.FlatStyle = FlatStyle.Flat;
        _channelComboBox.BackColor = Color.FromArgb(65, 65, 65);
        _channelComboBox.ForeColor = Color.White;
        _channelComboBox.Font = font;
        _channelComboBox.Location = new Point(20, y);
        _channelComboBox.Size = new Size(420, 28);
        foreach (var channel in SonarChannels.All)
        {
            _channelComboBox.Items.Add(channel.DisplayName);
        }

        Controls.Add(_channelComboBox);
        y += 32;

        // Sonar rejects writes to the mode it is not currently running in, so the picked
        // channel has to belong to the active mode.
        Controls.Add(CreateLabel("Sonar only accepts channels of the mode it is currently running in.",
            new Font("Tahoma", 8F),
            y));
        y += 26;

        if (_mode == SonarActionConfiguratorMode.ToggleMute)
        {
            ResumeLayout(false);
            return;
        }

        _valueLabel.Text = _mode == SonarActionConfiguratorMode.SetVolume
            ? "Volume (%)"
            : "Step (%), negative values lower the volume";
        Controls.Add(CreateLabel(_valueLabel.Text, font, y));
        y += 30;

        _valueUpDown.Font = font;
        _valueUpDown.BackColor = Color.FromArgb(65, 65, 65);
        _valueUpDown.ForeColor = Color.White;
        _valueUpDown.BorderStyle = BorderStyle.None;
        _valueUpDown.Location = new Point(20, y);
        _valueUpDown.Size = new Size(120, 28);
        _valueUpDown.Minimum = _mode == SonarActionConfiguratorMode.SetVolume ? 0 : -100;
        _valueUpDown.Maximum = 100;
        Controls.Add(_valueUpDown);

        ResumeLayout(false);
    }

    private static Label CreateLabel(string text, Font font, int y)
    {
        return new Label
        {
            Text = text,
            Font = font,
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, y)
        };
    }

    private void LoadConfiguration()
    {
        if (_mode == SonarActionConfiguratorMode.SwitchMode)
        {
            _modeComboBox.SelectedIndex = _configuration.Mode == SonarMode.Classic ? 0 : 1;
            return;
        }

        var index = SonarChannels.All.ToList().FindIndex(x => x.SliderId == _configuration.SliderId);
        _channelComboBox.SelectedIndex = index == -1 ? 0 : index;

        switch (_mode)
        {
            case SonarActionConfiguratorMode.SetVolume:
                _valueUpDown.Value = Math.Clamp(_configuration.VolumePercent, 0, 100);
                break;
            case SonarActionConfiguratorMode.AdjustVolume:
                _valueUpDown.Value = Math.Clamp(_configuration.StepPercent, -100, 100);
                break;
        }
    }

    public override bool OnActionSave()
    {
        if (_mode == SonarActionConfiguratorMode.SwitchMode)
        {
            _configuration.Mode = _modeComboBox.SelectedIndex == 1 ? SonarMode.Streamer : SonarMode.Classic;
            _action.Configuration = _configuration.Serialize();
            _action.ConfigurationSummary = _modeComboBox.SelectedIndex == 1 ? "Streamer" : "Classic";
            return true;
        }

        if (_channelComboBox.SelectedIndex < 0)
        {
            return false;
        }

        var channel = SonarChannels.All[_channelComboBox.SelectedIndex];
        _configuration.SliderId = channel.SliderId;

        switch (_mode)
        {
            case SonarActionConfiguratorMode.SetVolume:
                _configuration.VolumePercent = (int)_valueUpDown.Value;
                _action.ConfigurationSummary = $"{channel.DisplayName} -> {_configuration.VolumePercent}%";
                break;
            case SonarActionConfiguratorMode.AdjustVolume:
                _configuration.StepPercent = (int)_valueUpDown.Value;
                _action.ConfigurationSummary
                    = $"{channel.DisplayName} -> {_configuration.StepPercent:+#;-#;0}%";
                break;
            case SonarActionConfiguratorMode.ToggleMute:
                _action.ConfigurationSummary = channel.DisplayName;
                break;
        }

        _action.Configuration = _configuration.Serialize();
        return true;
    }
}
