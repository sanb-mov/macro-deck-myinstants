using System.Drawing.Drawing2D;
using SuchByte.MacroDeck.Sliders;

namespace SuchByte.MacroDeck.GUI.CustomControls.Sliders;

/// <summary>
/// A vertical volume slider with a title, a percentage readout and a mute toggle.
/// Used by the sliders view in the main window.
///
/// Three looks are supported, matching what the clients draw:
///   Classic - narrow track with a small pill thumb
///   Capsule - wide pill; the fill rises inside it and the mute is a speaker glyph
///   Knob    - wide track with a large ring knob riding on the fill
/// </summary>
public class SliderControl : UserControl
{
    private const int ThumbHeight = 18;
    private const int TitleHeight = 26;
    private const int ValueHeight = 22;
    private const int MuteButtonHeight = 28;
    private const int TrackPadding = 6;

    private MacroDeckSlider _slider;
    private SliderStyle _style;
    private bool _dragging;
    private Color _accentColor = Colors.AccentColor;

    /// <summary>
    /// Raised while the user drags the thumb and once when the drag ends
    /// </summary>
    public event EventHandler<double>? ValueChanged;

    /// <summary>
    /// Raised when the user clicks the mute button
    /// </summary>
    public event EventHandler<bool>? MuteToggled;

    public SliderControl(MacroDeckSlider slider, SliderStyle style = SliderStyle.Classic)
    {
        _slider = slider;
        _style = style;
        Size = new Size(96, 320);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Colors.Surface;
        ApplyAccentColor();
    }

    public string SliderId => _slider.SliderId;

    public SliderStyle Style
    {
        get => _style;
        set
        {
            if (_style == value)
            {
                return;
            }

            _style = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Replaces the state shown by this control. Ignored while the user is dragging so
    /// incoming updates cannot fight the mouse.
    /// </summary>
    public void UpdateSlider(MacroDeckSlider slider)
    {
        if (_dragging)
        {
            // Keep the mute state in sync, but leave the value alone until the drag ends
            _slider.Muted = slider.Muted;
            Invalidate();
            return;
        }

        _slider = slider;
        ApplyAccentColor();
        Invalidate();
    }

    private void ApplyAccentColor()
    {
        try
        {
            _accentColor = ColorTranslator.FromHtml(_slider.ColorHex);
        }
        catch
        {
            _accentColor = Colors.AccentColor;
        }
    }

    /// <summary>
    /// Width of the track, which is what mainly separates the three styles apart
    /// </summary>
    private int TrackWidth => _style switch
    {
        SliderStyle.Capsule => Math.Min((int)(Width * 0.55), 46),
        SliderStyle.Knob => Math.Min((int)(Width * 0.40), 34),
        _ => 10
    };

    private Rectangle TrackRectangle
    {
        get
        {
            var width = TrackWidth;
            var top = TitleHeight + TrackPadding;
            var bottom = Height - ValueHeight - MuteButtonHeight - TrackPadding;

            if (_style == SliderStyle.Knob)
            {
                // The knob is centred on the top of the fill, so at the extremes half
                // of it would spill past the track and cover the title or the
                // percentage. Inset both ends by its radius.
                top += (int)(width * 0.8);
                bottom -= (int)(width * 0.8);
            }

            return new Rectangle((Width - width) / 2, top, width, Math.Max(bottom - top, 1));
        }
    }

    private Rectangle MuteButtonRectangle =>
        new(8, Height - MuteButtonHeight - 4, Width - 16, MuteButtonHeight);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        using var titleFont = new Font("Tahoma", 9F, FontStyle.Bold);
        using var valueFont = new Font("Tahoma", 9F);
        using var textBrush = new SolidBrush(_slider.Muted ? Color.FromArgb(130, 130, 130) : Color.White);

        var centerFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        g.DrawString(_slider.DisplayName,
            titleFont,
            textBrush,
            new RectangleF(2, 2, Width - 4, TitleHeight),
            centerFormat);

        var track = TrackRectangle;
        var fillHeight = (int)Math.Round(track.Height * Math.Clamp(_slider.Value, 0.0, 1.0));
        var fillColor = _slider.Muted ? Color.FromArgb(90, 90, 90) : _accentColor;

        switch (_style)
        {
            case SliderStyle.Capsule:
                DrawCapsule(g, track, fillHeight, fillColor);
                break;
            case SliderStyle.Knob:
                DrawKnob(g, track, fillHeight, fillColor);
                break;
            default:
                DrawClassic(g, track, fillHeight, fillColor);
                break;
        }

        // Anchored to the control rather than to the track: the knob style insets the
        // track, and following it would put the text under the knob.
        g.DrawString($"{_slider.ValuePercent:0}%",
            valueFont,
            textBrush,
            new RectangleF(0, Height - MuteButtonHeight - ValueHeight - 4, Width, ValueHeight),
            centerFormat);

        if (!_slider.SupportsMute)
        {
            return;
        }

        if (_style == SliderStyle.Capsule)
        {
            DrawSpeakerMute(g);
            return;
        }

        var muteRectangle = MuteButtonRectangle;
        var muteBackColor = _slider.Muted ? Color.FromArgb(200, 60, 60) : Colors.Surface2;
        using (var muteBrush = new SolidBrush(muteBackColor))
        using (var mutePath = RoundedRectangle(muteRectangle, 6))
        {
            g.FillPath(muteBrush, mutePath);
        }

        using var muteTextBrush = new SolidBrush(Color.White);
        g.DrawString(_slider.Muted ? "Muted" : "Mute", valueFont, muteTextBrush, muteRectangle, centerFormat);
    }

    private void DrawClassic(Graphics g, Rectangle track, int fillHeight, Color fillColor)
    {
        var radius = track.Width / 2;

        using (var trackBrush = new SolidBrush(Colors.Surface3))
        using (var trackPath = RoundedRectangle(track, radius))
        {
            g.FillPath(trackBrush, trackPath);
        }

        if (fillHeight > 0)
        {
            var fill = new Rectangle(track.X, track.Bottom - fillHeight, track.Width, fillHeight);
            using var fillBrush = new SolidBrush(fillColor);
            using var fillPath = RoundedRectangle(fill, radius);
            g.FillPath(fillBrush, fillPath);
        }

        var thumbY = track.Bottom - fillHeight - ThumbHeight / 2;
        var thumb = new Rectangle(track.X - 7, thumbY, track.Width + 14, ThumbHeight);
        using var thumbBrush = new SolidBrush(_slider.Muted ? Color.FromArgb(120, 120, 120) : Color.White);
        using var thumbPath = RoundedRectangle(thumb, 6);
        g.FillPath(thumbBrush, thumbPath);
    }

    /// <summary>
    /// Wide pill whose fill rises inside it; the fill is clipped to the pill so the
    /// rounded bottom stays rounded and the top edge of the fill is flat.
    /// </summary>
    private void DrawCapsule(Graphics g, Rectangle track, int fillHeight, Color fillColor)
    {
        var radius = track.Width / 2;

        using var capsule = RoundedRectangle(track, radius);

        using (var trackBrush = new SolidBrush(Colors.Surface3))
        {
            g.FillPath(trackBrush, capsule);
        }

        if (fillHeight > 0)
        {
            var previousClip = g.Clip;
            g.SetClip(capsule);
            using (var fillBrush = new SolidBrush(fillColor))
            {
                g.FillRectangle(fillBrush, track.X, track.Bottom - fillHeight, track.Width, fillHeight);
            }

            g.Clip = previousClip;
        }

        using var outline = new Pen(Color.FromArgb(70, 70, 70), 1.5f);
        g.DrawPath(outline, capsule);
    }

    /// <summary>
    /// Wide track with a large ring knob sitting on the fill level
    /// </summary>
    private void DrawKnob(Graphics g, Rectangle track, int fillHeight, Color fillColor)
    {
        var radius = Math.Max((int)(track.Width * 0.35), 4);

        using (var trackBrush = new SolidBrush(Colors.Surface3))
        using (var trackPath = RoundedRectangle(track, radius))
        {
            g.FillPath(trackBrush, trackPath);
        }

        if (fillHeight > 0)
        {
            var fill = new Rectangle(track.X, track.Bottom - fillHeight, track.Width, fillHeight);
            using var fillBrush = new SolidBrush(fillColor);
            using var fillPath = RoundedRectangle(fill, radius);
            g.FillPath(fillBrush, fillPath);
        }

        var knobDiameter = (int)(track.Width * 1.55);
        var knobCenterY = track.Bottom - fillHeight;
        var knob = new Rectangle(track.X + track.Width / 2 - knobDiameter / 2,
            knobCenterY - knobDiameter / 2,
            knobDiameter,
            knobDiameter);

        using (var ringBrush = new SolidBrush(_slider.Muted ? Color.FromArgb(150, 150, 150) : Color.White))
        {
            g.FillEllipse(ringBrush, knob);
        }

        var inner = knob;
        inner.Inflate(-Math.Max(knobDiameter / 5, 2), -Math.Max(knobDiameter / 5, 2));
        using var centerBrush = new SolidBrush(_slider.Muted ? Color.FromArgb(90, 90, 90) : Colors.Surface);
        g.FillEllipse(centerBrush, inner);
    }

    /// <summary>
    /// Speaker glyph used as the mute toggle in the capsule style, crossed out when muted
    /// </summary>
    private void DrawSpeakerMute(Graphics g)
    {
        var area = MuteButtonRectangle;
        var size = Math.Min(area.Height, 22);
        var centerX = Width / 2;
        var centerY = area.Y + area.Height / 2;

        var color = _slider.Muted ? Color.FromArgb(200, 60, 60) : Color.White;
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, 1.8f);

        // Cone: a small box plus a triangle, as in a classic speaker icon
        var boxWidth = size * 0.22f;
        var boxHeight = size * 0.36f;
        var left = centerX - size * 0.42f;
        g.FillRectangle(brush, left, centerY - boxHeight / 2, boxWidth, boxHeight);

        var cone = new[]
        {
            new PointF(left + boxWidth, centerY - boxHeight / 2),
            new PointF(left + boxWidth + size * 0.28f, centerY - size * 0.40f),
            new PointF(left + boxWidth + size * 0.28f, centerY + size * 0.40f),
            new PointF(left + boxWidth, centerY + boxHeight / 2)
        };
        g.FillPolygon(brush, cone);

        if (_slider.Muted)
        {
            var slashLeft = centerX + size * 0.02f;
            g.DrawLine(pen, slashLeft, centerY - size * 0.28f, slashLeft + size * 0.36f, centerY + size * 0.28f);
            g.DrawLine(pen, slashLeft, centerY + size * 0.28f, slashLeft + size * 0.36f, centerY - size * 0.28f);
            return;
        }

        // Two arcs for the sound waves
        var waveX = centerX + size * 0.02f;
        g.DrawArc(pen, waveX, centerY - size * 0.26f, size * 0.26f, size * 0.52f, -60, 120);
        g.DrawArc(pen, waveX, centerY - size * 0.42f, size * 0.46f, size * 0.84f, -60, 120);
    }

    private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(radius * 2, 1);

        if (rectangle.Width <= diameter || rectangle.Height <= diameter)
        {
            path.AddRectangle(rectangle);
            return path;
        }

        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (_slider.SupportsMute && MuteButtonRectangle.Contains(e.Location))
        {
            MuteToggled?.Invoke(this, !_slider.Muted);
            return;
        }

        _dragging = true;
        SetValueFromMouse(e.Y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            SetValueFromMouse(e.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ValueChanged?.Invoke(this, _slider.Value);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        var step = e.Delta > 0 ? 0.02 : -0.02;
        var value = Math.Clamp(_slider.Value + step, 0.0, 1.0);
        if (Math.Abs(value - _slider.Value) < 0.0001)
        {
            return;
        }

        _slider.Value = value;
        Invalidate();
        ValueChanged?.Invoke(this, value);
    }

    private void SetValueFromMouse(int mouseY)
    {
        var track = TrackRectangle;
        var value = Math.Clamp((track.Bottom - mouseY) / (double)track.Height, 0.0, 1.0);
        value = Math.Round(value, 2);

        if (Math.Abs(value - _slider.Value) < 0.0001)
        {
            return;
        }

        _slider.Value = value;
        Invalidate();
        ValueChanged?.Invoke(this, value);
    }
}
