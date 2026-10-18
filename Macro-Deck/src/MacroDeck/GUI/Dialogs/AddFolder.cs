using System.Diagnostics;
using SuchByte.MacroDeck.Device;
using SuchByte.MacroDeck.Folders;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.Language;
using SuchByte.MacroDeck.Profiles;
using SuchByte.MacroDeck.Server;
using SuchByte.MacroDeck.Sliders;
using MessageBox = SuchByte.MacroDeck.GUI.CustomControls.MessageBox;

namespace SuchByte.MacroDeck.GUI.Dialogs;

public partial class AddFolder : DialogForm
{
    private MacroDeckFolder ParentFolder;
    private MacroDeckFolder Folder;

    private readonly CheckBox _sliderFolderCheckBox = new();
    private readonly CheckBox _soundboardFolderCheckBox = new();
    private readonly System.Windows.Forms.ComboBox _sliderProviderComboBox = new();
    private readonly System.Windows.Forms.ComboBox _sliderStyleComboBox = new();

    public AddFolder(MacroDeckFolder parentFolder)
    {
        ParentFolder = parentFolder;
        InitializeComponent();
        BuildSliderFolderControls();
        UpdateTranslation();
        btnCreateFolder.Text = LanguageManager.Strings.Create;
    }

    public AddFolder(MacroDeckFolder folder, bool rename)
    {
        Folder = folder;
        InitializeComponent();
        BuildSliderFolderControls();
        UpdateTranslation();
        btnCreateFolder.Text = LanguageManager.Strings.Save;
        folderName.Enabled = !folder.IsRootFolder;
    }

    /// <summary>
    /// Adds the "slider folder" option. Built in code so the designer file stays untouched.
    /// </summary>
    private void BuildSliderFolderControls()
    {
        SuspendLayout();

        const int offset = 68;

        _sliderFolderCheckBox.Text = "Slider folder (shows sliders instead of buttons)";
        _sliderFolderCheckBox.Font = new Font("Tahoma", 9.75F);
        _sliderFolderCheckBox.ForeColor = Color.White;
        _sliderFolderCheckBox.AutoSize = true;
        _sliderFolderCheckBox.Location = new Point(14, 46);
        _sliderFolderCheckBox.CheckedChanged += SliderFolderCheckBoxOnCheckedChanged;
        Controls.Add(_sliderFolderCheckBox);

        _sliderProviderComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sliderProviderComboBox.FlatStyle = FlatStyle.Flat;
        _sliderProviderComboBox.BackColor = Color.FromArgb(65, 65, 65);
        _sliderProviderComboBox.ForeColor = Color.White;
        _sliderProviderComboBox.Font = new Font("Tahoma", 9F);
        _sliderProviderComboBox.Location = new Point(14, 68);
        _sliderProviderComboBox.Size = new Size(273, 25);
        _sliderProviderComboBox.Enabled = false;
        Controls.Add(_sliderProviderComboBox);

        _sliderStyleComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sliderStyleComboBox.FlatStyle = FlatStyle.Flat;
        _sliderStyleComboBox.BackColor = Color.FromArgb(65, 65, 65);
        _sliderStyleComboBox.ForeColor = Color.White;
        _sliderStyleComboBox.Font = new Font("Tahoma", 9F);
        _sliderStyleComboBox.Location = new Point(297, 68);
        _sliderStyleComboBox.Size = new Size(159, 25);
        _sliderStyleComboBox.Enabled = false;
        _sliderStyleComboBox.Items.AddRange(["Classic", "Capsule", "Knob"]);
        _sliderStyleComboBox.SelectedIndex = 0;
        Controls.Add(_sliderStyleComboBox);

        foreach (var provider in SliderManager.Providers)
        {
            _sliderProviderComboBox.Items.Add(provider.DisplayName);
        }

        if (_sliderProviderComboBox.Items.Count > 0)
        {
            _sliderProviderComboBox.SelectedIndex = 0;
        }
        else
        {
            _sliderFolderCheckBox.Enabled = false;
            _sliderFolderCheckBox.Text = "Slider folder (no slider provider is installed)";
        }

        _soundboardFolderCheckBox.Text = "Soundboard folder (trending sounds of MyInstants as buttons)";
        _soundboardFolderCheckBox.Font = new Font("Tahoma", 9.75F);
        _soundboardFolderCheckBox.ForeColor = Color.White;
        _soundboardFolderCheckBox.AutoSize = true;
        _soundboardFolderCheckBox.Location = new Point(14, 98);
        _soundboardFolderCheckBox.CheckedChanged += (_, _) =>
        {
            // A folder is either buttons, sliders or sounds
            if (_soundboardFolderCheckBox.Checked && _sliderFolderCheckBox.Checked)
            {
                _sliderFolderCheckBox.Checked = false;
            }
        };
        Controls.Add(_soundboardFolderCheckBox);

        groupAutomaticallySwitchFolder.Top += offset;
        btnCreateFolder.Top += offset;
        ClientSize = ClientSize with { Height = ClientSize.Height + offset };

        Controls.SetChildIndex(_sliderFolderCheckBox, 0);
        Controls.SetChildIndex(_soundboardFolderCheckBox, 0);
        Controls.SetChildIndex(_sliderProviderComboBox, 0);
        Controls.SetChildIndex(_sliderStyleComboBox, 0);

        ResumeLayout();
    }

    private void SliderFolderCheckBoxOnCheckedChanged(object? sender, EventArgs e)
    {
        _sliderProviderComboBox.Enabled = _sliderFolderCheckBox.Checked;
        _sliderStyleComboBox.Enabled = _sliderFolderCheckBox.Checked;

        if (_sliderFolderCheckBox.Checked && _soundboardFolderCheckBox.Checked)
        {
            _soundboardFolderCheckBox.Checked = false;
        }
    }

    /// <summary>
    /// Provider id currently picked in the combo box, or "" when none is selected
    /// </summary>
    private string GetSelectedSliderProviderId()
    {
        var providers = SliderManager.Providers.ToList();
        var index = _sliderProviderComboBox.SelectedIndex;
        return index >= 0 && index < providers.Count ? providers[index].ProviderId : "";
    }

    private void UpdateTranslation()
    {
        lblFolderName.Text = LanguageManager.Strings.FolderName;
        lblApplication.Text = LanguageManager.Strings.Application;
        lblDevices.Text = LanguageManager.Strings.Devices;
        groupAutomaticallySwitchFolder.Text = LanguageManager.Strings.AutomaticallySwitchToFolder;
        radioNever.Text = LanguageManager.Strings.Never;
        radioOnFocus.Text = LanguageManager.Strings.OnApplicationFocus;
    }

    private void BtnCreateFolder_Click(object sender, EventArgs e)
    {
        if (folderName.Text.Length < 1)
        {
            return;
        }

        if (Folder == null)
        {
            if (ProfileManager.CurrentProfile.Folders.Find(x =>
                    x.DisplayName.Equals(folderName.Text, StringComparison.OrdinalIgnoreCase)) !=
                null)
            {
                using var msgBox = new MessageBox();
                msgBox.ShowDialog(LanguageManager.Strings.CantCreateFolder,
                    string.Format(LanguageManager.Strings.FolderCalledXAlreadyExists, folderName.Text),
                    MessageBoxButtons.OK);
                msgBox.Dispose();
                return;
            }

            if (ParentFolder == null || !ProfileManager.CurrentProfile.Folders.Contains(ParentFolder))
            {
                ParentFolder = ProfileManager.CurrentProfile.Folders[0];
            }

            Folder = ProfileManager.CreateFolder(folderName.Text, ParentFolder, ProfileManager.CurrentProfile);
        }
        else
        {
            if (ProfileManager.CurrentProfile.Folders.Find(x =>
                    x.DisplayName.Equals(folderName.Text, StringComparison.OrdinalIgnoreCase)) !=
                null &&
                ProfileManager.CurrentProfile.Folders.Find(x =>
                    x.DisplayName.Equals(folderName.Text, StringComparison.OrdinalIgnoreCase)) !=
                Folder)
            {
                using var msgBox = new MessageBox();
                msgBox.ShowDialog(LanguageManager.Strings.CantCreateFolder,
                    string.Format(LanguageManager.Strings.FolderCalledXAlreadyExists, folderName.Text),
                    MessageBoxButtons.OK);
                msgBox.Dispose();
                return;
            }
        }

        Folder.DisplayName = folderName.Text;
        Folder.ApplicationToTrigger = applicationList.Text;

        if (!Folder.IsRootFolder)
        {
            Folder.IsSliderFolder = _sliderFolderCheckBox.Checked;
            Folder.SliderProviderId = _sliderFolderCheckBox.Checked ? GetSelectedSliderProviderId() : "";
            Folder.SliderStyle = (SliderStyle)Math.Max(_sliderStyleComboBox.SelectedIndex, 0);
            Folder.IsSoundboardFolder = _soundboardFolderCheckBox.Checked;
        }

        if (radioOnFocus.Checked && applicationList.Text.Length > 0 && devicesList.CheckedItems.Count > 0)
        {
            Folder.ApplicationToTrigger = applicationList.Text;
            Folder.ApplicationsFocusDevices = new List<string>();
            foreach (int i in devicesList.CheckedIndices)
            {
                Folder.ApplicationsFocusDevices.Add(DeviceManager
                    .GetMacroDeckDeviceByDisplayName(devicesList.Items[i].ToString()).ClientId);
            }
        }
        else
        {
            Folder.ApplicationToTrigger = "";
            Folder.ApplicationsFocusDevices = new List<string>();
        }

        ProfileManager.Save();

        // Clients sitting in this folder cached its slider set and style when they
        // entered it; without this they keep the old look until they navigate away.
        MacroDeckServer.UpdateFolder(Folder);

        DialogResult = DialogResult.OK;
        Close();
    }

    private void AddFolder_Load(object sender, EventArgs e)
    {
        if (Folder != null)
        {
            folderName.Text = Folder.DisplayName;

            _sliderFolderCheckBox.Enabled = _sliderFolderCheckBox.Enabled && !Folder.IsRootFolder;
            _sliderFolderCheckBox.Checked = Folder.IsSliderFolder;
            _soundboardFolderCheckBox.Enabled = !Folder.IsRootFolder;
            _soundboardFolderCheckBox.Checked = Folder.IsSoundboardFolder;
            _sliderStyleComboBox.SelectedIndex = (int)Folder.SliderStyle;
            if (!string.IsNullOrWhiteSpace(Folder.SliderProviderId))
            {
                var providers = SliderManager.Providers.ToList();
                var index = providers.FindIndex(x => x.ProviderId == Folder.SliderProviderId);
                if (index >= 0)
                {
                    _sliderProviderComboBox.SelectedIndex = index;
                }
            }

            if (Folder.ApplicationToTrigger.Length > 0 && Folder.ApplicationsFocusDevices.Count > 0)
            {
                radioOnFocus.CheckedChanged -= RadioOnFocus_CheckedChanged;
                radioOnFocus.Checked = true;
                radioOnFocus.CheckedChanged += RadioOnFocus_CheckedChanged;
            }
        }
    }

    private void RadioOnFocus_CheckedChanged(object sender, EventArgs e)
    {
        if (radioOnFocus.Checked && !radioNever.Checked)
        {
            applicationDeviceSettings.Enabled = true;
            Task.Run(() => { LoadApplications(); });
            LoadDevices();
        }
        else
        {
            applicationDeviceSettings.Enabled = false;
            devicesList.Items.Clear();
        }
    }

    private void LoadApplications()
    {
        var processCollection = Process.GetProcesses();
        foreach (var p in processCollection)
        {
            Invoke(() =>
            {
                if (!applicationList.Items.Contains(p.ProcessName) && !string.IsNullOrEmpty(p.MainWindowTitle))
                {
                    applicationList.Items.Add(p.ProcessName);
                }
            });
        }

        if (Folder != null)
        {
            Invoke(() =>
            {
                if (Folder.ApplicationToTrigger.Length > 0 &&
                    !applicationList.Items.Contains(Folder.ApplicationToTrigger))
                {
                    applicationList.Items.Add(Folder.ApplicationToTrigger);
                }

                applicationList.Text = Folder.ApplicationToTrigger;
            });
        }
    }

    private void LoadDevices()
    {
        devicesList.Items.Clear();

        foreach (var macroDeckDevice in DeviceManager.GetKnownDevices())
        {
            devicesList.Items.Add(macroDeckDevice.DisplayName,
                Folder != null &&
                Folder.ApplicationsFocusDevices != null &&
                Folder.ApplicationsFocusDevices.Contains(macroDeckDevice.ClientId));
        }
    }

    private void BtnReloadApplications_Click(object sender, EventArgs e)
    {
        Task.Run(() => { LoadApplications(); });
    }
}
