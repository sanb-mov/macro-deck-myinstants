using SuchByte.MacroDeck.Soundboard;

namespace SuchByte.MacroDeck.GUI.MainWindowViews;

/// <summary>
/// Main window view that shows the sound buttons of myinstants.com (trending, or the results of a search)
/// one page at a time and plays them on this PC when clicked.
/// </summary>
public class SoundboardView : UserControl
{
    private static readonly Color NavColor = Color.FromArgb(0, 103, 205);
    private const int ButtonMargin = 5;

    // How often the shown page is checked for new sounds on myinstants.com
    private const int AutoRefreshSeconds = 120;

    private static readonly Color StopColor = Color.FromArgb(190, 50, 50);
    private static readonly Color HighlightColor = Color.FromArgb(200, 140, 0);

    private readonly FlowLayoutPanel _soundsPanel;
    private readonly Label _statusLabel;
    private readonly Label _pageLabel;
    private readonly System.Windows.Forms.Button _reloadButton;
    private readonly System.Windows.Forms.Button _previousButton;
    private readonly System.Windows.Forms.Button _nextButton;
    private readonly System.Windows.Forms.TextBox _searchBox;
    private readonly NumericUpDown _rowsBox;
    private readonly NumericUpDown _columnsBox;
    private readonly System.Windows.Forms.Timer _settingsTimer;
    private readonly System.Windows.Forms.Timer _autoRefreshTimer;
    private readonly List<System.Windows.Forms.Button> _soundButtons = [];

    private string _query = "";
    private int _page = 1;
    private bool _hasMore;
    private bool _loading;
    private string _shownIds = "";

    /// <param name="embedded">
    /// Shown inside the deck as the content of a soundboard folder: only the sounds and the page bar,
    /// without the title, reload and search row
    /// </param>
    public SoundboardView(bool embedded = false)
    {
        Dock = DockStyle.Fill;
        BackColor = Colors.Background;
        Name = "Soundboard";

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = Colors.Background
        };

        header.Controls.Add(new Label
        {
            Text = "MyInstants",
            Font = new Font("Tahoma", 15F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(16, 14)
        });

        _reloadButton = CreateButton("Reload");
        _reloadButton.Location = new Point(170, 14);
        _reloadButton.Size = new Size(90, 30);
        _reloadButton.Click += (_, _) => _ = LoadPageAsync(_page, true);
        header.Controls.Add(_reloadButton);

        _searchBox = new System.Windows.Forms.TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Colors.Surface2,
            ForeColor = Color.White,
            Font = new Font("Tahoma", 10F),
            PlaceholderText = "Search MyInstants...",
            Location = new Point(380, 17),
            Size = new Size(240, 28)
        };
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Search();
            }
        };
        header.Controls.Add(_searchBox);

        var searchButton = CreateButton("Search");
        searchButton.Location = new Point(630, 14);
        searchButton.Size = new Size(90, 30);
        searchButton.Click += (_, _) => Search();
        header.Controls.Add(searchButton);

        _statusLabel = new Label
        {
            Font = new Font("Tahoma", 10F),
            ForeColor = Color.FromArgb(200, 160, 60),
            AutoSize = true,
            Location = new Point(734, 22)
        };
        header.Controls.Add(_statusLabel);

        // Page navigation, in its own color so it stands out from the sound buttons
        // A flow layout keeps everything on one line and compact, so the bar also fits the narrower
        // panel of the deck view
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(12, 9, 0, 0),
            BackColor = Colors.Background
        };

        _previousButton = CreateButton("< Previous", NavColor);
        _previousButton.Size = new Size(110, 34);
        _previousButton.Margin = new Padding(0, 0, 8, 0);
        _previousButton.Click += (_, _) => _ = LoadPageAsync(_page - 1, false);
        footer.Controls.Add(_previousButton);

        _pageLabel = new Label
        {
            Font = new Font("Tahoma", 10F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            MinimumSize = new Size(64, 0),
            Margin = new Padding(0, 8, 8, 0)
        };
        footer.Controls.Add(_pageLabel);

        _nextButton = CreateButton("Next >", NavColor);
        _nextButton.Size = new Size(110, 34);
        _nextButton.Margin = new Padding(0, 0, 8, 0);
        _nextButton.Click += (_, _) => _ = LoadPageAsync(_page + 1, false);
        footer.Controls.Add(_nextButton);

        var stopButton = CreateButton("Stop sound", StopColor);
        stopButton.Size = new Size(110, 34);
        stopButton.Margin = new Padding(0, 0, 16, 0);
        stopButton.Click += (_, _) => SoundPlayerService.Stop();
        footer.Controls.Add(stopButton);

        // Size of a page: the sounds are laid out over the pages according to rows x columns
        footer.Controls.Add(new Label
        {
            Text = "Rows",
            ForeColor = Color.White,
            Font = new Font("Tahoma", 10F),
            AutoSize = true,
            Margin = new Padding(0, 8, 4, 0)
        });
        _rowsBox = CreateNumberBox(SoundboardSettings.Rows, SoundboardSettings.MaxRows);
        _rowsBox.Margin = new Padding(0, 5, 12, 0);
        footer.Controls.Add(_rowsBox);

        footer.Controls.Add(new Label
        {
            Text = "Columns",
            ForeColor = Color.White,
            Font = new Font("Tahoma", 10F),
            AutoSize = true,
            Margin = new Padding(0, 8, 4, 0)
        });
        _columnsBox = CreateNumberBox(SoundboardSettings.Columns, SoundboardSettings.MaxColumns);
        _columnsBox.Margin = new Padding(0, 5, 0, 0);
        footer.Controls.Add(_columnsBox);

        // Wait a moment after the last change so spinning the value does not reload on every step
        _settingsTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _settingsTimer.Tick += (_, _) =>
        {
            _settingsTimer.Stop();
            SoundboardSettings.Set((int)_rowsBox.Value, (int)_columnsBox.Value);
            _ = LoadPageAsync(1, false);
        };
        // New sounds on the site show up on their own while this view is open
        _autoRefreshTimer = new System.Windows.Forms.Timer { Interval = AutoRefreshSeconds * 1000 };
        _autoRefreshTimer.Tick += (_, _) => _ = LoadPageAsync(_page, true, true);
        _autoRefreshTimer.Start();

        _rowsBox.ValueChanged += (_, _) => _settingsTimer.Stop();
        _rowsBox.ValueChanged += (_, _) => _settingsTimer.Start();
        _columnsBox.ValueChanged += (_, _) => _settingsTimer.Stop();
        _columnsBox.ValueChanged += (_, _) => _settingsTimer.Start();

        _soundsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = false,
            Padding = new Padding(12),
            BackColor = Colors.Background
        };
        _soundsPanel.SizeChanged += (_, _) => ResizeSoundButtons();

        Controls.Add(_soundsPanel);
        Controls.Add(footer);
        if (!embedded)
        {
            Controls.Add(header);
        }

        UpdateNavigation();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settingsTimer.Dispose();
            _autoRefreshTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _ = LoadPageAsync(1, false);
    }

    private void Search()
    {
        _query = _searchBox.Text.Trim();
        _ = LoadPageAsync(1, false);
    }

    /// <summary>
    /// Replaces the shown sounds with the given page of the current query, so only one page
    /// of buttons exists at a time.
    /// </summary>
    /// <param name="silent">
    /// Background refresh of the shown page: nothing is blocked or announced unless new sounds arrived
    /// </param>
    private async Task LoadPageAsync(int page, bool forceRefresh, bool silent = false)
    {
        if (_loading || page < 1)
        {
            return;
        }

        if (!silent)
        {
            _loading = true;
            _statusLabel.Text = "Loading...";
            UpdateNavigation();
        }

        var query = _query;
        var result = await MyInstantsService.GetSoundPageAsync(query, page, SoundboardSettings.PageSize, forceRefresh);
        var sounds = result.Sounds;

        if (!silent)
        {
            _loading = false;
        }

        if (IsDisposed || !IsHandleCreated || query != _query)
        {
            return;
        }

        if (silent)
        {
            // The user moved on or a manual load is running, or the site could not be reached
            // or had nothing new: keep what is on screen
            var ids = string.Join(",", sounds.Select(x => x.Id));
            if (_loading || page != _page || sounds.Count == 0 || ids == _shownIds)
            {
                return;
            }

            _statusLabel.Text = "New sounds added";
        }

        // Past the last page there is nothing to show: stay on the page we were on
        if (sounds.Count == 0 && page > 1)
        {
            _hasMore = false;
            _statusLabel.Text = "No more sounds";
            UpdateNavigation();
            return;
        }

        _page = page;
        _hasMore = result.HasMore;
        _shownIds = string.Join(",", sounds.Select(x => x.Id));

        _soundsPanel.SuspendLayout();
        foreach (var old in _soundsPanel.Controls.OfType<Control>().ToList())
        {
            _soundsPanel.Controls.Remove(old);
            old.Dispose();
        }

        _soundButtons.Clear();

        for (var i = 0; i < sounds.Count; i++)
        {
            var sound = sounds[i];
            var button = CreateButton(sound.Name);
            button.Margin = new Padding(ButtonMargin);
            button.Font = new Font("Tahoma", 9F, FontStyle.Bold);

            // In a search the best match comes first, highlighted
            if (i == 0 && page == 1 && query.Length > 0)
            {
                button.BackColor = HighlightColor;
            }

            button.Click += (_, _) => SoundPlayerService.Play(sound);
            _soundButtons.Add(button);
            _soundsPanel.Controls.Add(button);
        }

        ResizeSoundButtons();
        _soundsPanel.ResumeLayout();

        if (silent)
        {
            UpdateNavigation();
            return;
        }

        _statusLabel.Text = sounds.Count > 0
            ? ""
            : query.Length == 0
                ? "Could not load sounds from myinstants.com"
                : "No sounds found";
        UpdateNavigation();
    }

    /// <summary>
    /// Sizes the buttons so that rows x columns of them fill the panel exactly
    /// </summary>
    private void ResizeSoundButtons()
    {
        var columns = SoundboardSettings.Columns;
        var rows = SoundboardSettings.Rows;

        var width = (_soundsPanel.ClientSize.Width - _soundsPanel.Padding.Horizontal) / columns - ButtonMargin * 2;
        var height = (_soundsPanel.ClientSize.Height - _soundsPanel.Padding.Vertical) / rows - ButtonMargin * 2;
        var size = new Size(Math.Max(width - 1, 40), Math.Max(height - 1, 30));

        foreach (var button in _soundButtons)
        {
            button.Size = size;
        }
    }

    private void UpdateNavigation()
    {
        _reloadButton.Enabled = !_loading;
        _previousButton.Enabled = !_loading && _page > 1;
        _nextButton.Enabled = !_loading && _hasMore;
        _pageLabel.Text = $"Page {_page}";
    }

    private static NumericUpDown CreateNumberBox(int value, int max)
    {
        return new NumericUpDown
        {
            Minimum = 1,
            Maximum = max,
            Value = Math.Clamp(value, 1, max),
            Size = new Size(54, 28),
            BackColor = Colors.Surface2,
            ForeColor = Color.White,
            Font = new Font("Tahoma", 10F),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private static System.Windows.Forms.Button CreateButton(string text, Color? color = null)
    {
        var button = new System.Windows.Forms.Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = color ?? Colors.Surface2,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            // Sound names may contain '&', which a button would swallow as a keyboard shortcut marker
            UseMnemonic = false,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}
