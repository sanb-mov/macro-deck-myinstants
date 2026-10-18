package com.suchbyte.macrodeck.legacy;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.BitmapShader;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.os.Handler;
import android.view.MotionEvent;
import android.view.View;

import java.util.List;

/**
 * The whole deck, drawn as a single custom view.
 *
 * A ViewGroup of per-button child views would work too, but on a 2013 device a
 * flat canvas is markedly cheaper: no layout pass when the host pushes an
 * update, and no view recycling to get wrong when the grid size changes.
 *
 * The geometry deliberately reproduces widget-grid.component.ts: square buttons
 * sized to min(width/columns, height/rows), centred in the view, with spacing
 * and corner radius expressed as a percentage of the button size.
 */
public class DeckView extends View {

    /** Colour the web client uses for a cell the host sent no button for. */
    private static final int EMPTY_BACKGROUND = 0xFF232323;

    /** CSS `.pressed { transform: scale(0.9) }` from button-widget.component.scss. */
    private static final float PRESSED_SCALE = 0.9f;

    /** Card behind a slider, matching the web client's .slider-widget-wrapper. */
    private static final int SLIDER_CARD = 0xFF232323;
    private static final int SLIDER_TRACK = 0xFF3E3E3E;
    private static final int SLIDER_MUTE_BG = 0xFF323232;
    private static final int SLIDER_MUTED_BG = 0xFFC83C3C;
    private static final int SLIDER_MUTED_FILL = 0xFF5A5A5A;
    private static final int SLIDER_DIM_TEXT = 0xFF828282;
    private static final int SLIDER_CAPSULE_EDGE = 0xFF464646;
    private static final int BACK_BUTTON_BG = 0xCC323232;

    /** Only send while dragging once the value moved this much (1%). */
    private static final double SLIDER_SEND_STEP = 0.01;

    public interface InteractionListener {
        void onInteraction(String method, int x, int y);

        /** A slider was dragged to a new value (0.0 - 1.0). */
        void onSliderChange(MdSlider slider, double value);

        /** The mute button of a slider was tapped. */
        void onSliderMute(MdSlider slider, boolean muted);

        /** The on-screen back arrow of a slider folder was tapped. */
        void onBack();
    }

    private final Paint fillPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint imagePaint = new Paint(Paint.ANTI_ALIAS_FLAG | Paint.FILTER_BITMAP_FLAG);
    private final Paint textPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint strokePaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF rect = new RectF();
    private final RectF cell = new RectF();
    private final RectF part = new RectF();
    private final RectF track = new RectF();
    private final RectF backButton = new RectF();
    private final Path clipPath = new Path();
    private final Matrix shaderMatrix = new Matrix();
    private final Handler handler = new Handler();

    private MdClient client;
    private InteractionListener interactionListener;
    private int longPressDelay = 1000;

    private int pressedX = -1;
    private int pressedY = -1;
    private boolean longPressFired;

    // Slider drag state. While dragging, incoming UPDATE_SLIDER values for this
    // slider are already applied to the model, but the drag keeps ownership of
    // the value so the thumb cannot jump under the finger.
    private MdSlider draggingSlider;
    private double lastSentValue = -1;

    private final Runnable longPressRunnable = new Runnable() {
        @Override
        public void run() {
            if (pressedX < 0) {
                return;
            }
            longPressFired = true;
            emit("BUTTON_LONG_PRESS");
        }
    };

    public DeckView(Context context) {
        super(context);
        setBackgroundColor(Color.BLACK);
        strokePaint.setStyle(Paint.Style.STROKE);
        strokePaint.setStrokeCap(Paint.Cap.ROUND);
    }

    public void setClient(MdClient client) {
        this.client = client;
        invalidate();
    }

    public void setInteractionListener(InteractionListener listener) {
        this.interactionListener = listener;
    }

    public void setLongPressDelay(int delay) {
        this.longPressDelay = delay;
    }

    /** Cancels any in-flight press, e.g. when the connection drops. */
    public void cancelPress() {
        handler.removeCallbacks(longPressRunnable);
        pressedX = -1;
        pressedY = -1;
        longPressFired = false;
        draggingSlider = null;
        lastSentValue = -1;
        invalidate();
    }

    // ----------------------------------------------------------------- draw

    @Override
    protected void onDraw(Canvas canvas) {
        super.onDraw(canvas);
        if (client == null || client.rows <= 0 || client.columns <= 0) {
            return;
        }

        int width = getWidth();
        int height = getHeight();
        float buttonSize = Math.min((float) width / client.columns, (float) height / client.rows);
        if (buttonSize <= 0) {
            return;
        }

        if (client.hasSliders()) {
            drawSliders(canvas, buttonSize);
            return;
        }

        // widget-grid.component.ts computes these in points and CSS converts them
        // back to pixels; the 72/96 round trip cancels out, so work in pixels.
        float margin = (float) ((client.buttonSpacing / 100.0) * buttonSize) / 2f;
        float radius = (float) ((client.buttonRadius / 100.0) * buttonSize) / 2f;

        float xOffset = (width - client.columns * buttonSize) / 2f;
        float yOffset = (height - client.rows * buttonSize) / 2f;

        for (int row = 0; row < client.rows; row++) {
            for (int column = 0; column < client.columns; column++) {
                MdButton button = client.getButton(column, row);

                float left = xOffset + column * buttonSize + margin;
                float top = yOffset + row * buttonSize + margin;
                float side = buttonSize - (margin * 2f);
                if (side <= 0) {
                    continue;
                }
                rect.set(left, top, left + side, top + side);

                float drawRadius = radius;
                if (column == pressedX && row == pressedY) {
                    float inset = (side * (1f - PRESSED_SCALE)) / 2f;
                    rect.inset(inset, inset);
                    drawRadius = radius * PRESSED_SCALE;
                }

                drawButton(canvas, button, drawRadius);
            }
        }
    }

    // --------------------------------------------------------------- sliders

    /**
     * Lays a slider out inside the grid cells the host assigned it: a title, a
     * vertical track with a thumb, the percentage, and a mute button.
     *
     * Everything is proportional to the cell so the same code fits a 7" tablet
     * and a phone; the caps keep the text from ballooning on a tall cell.
     */
    private void drawSliders(Canvas canvas, float buttonSize) {
        float margin = (float) ((client.buttonSpacing / 100.0) * buttonSize) / 2f;
        float radius = (float) ((client.buttonRadius / 100.0) * buttonSize) / 2f;

        float xOffset = (getWidth() - client.columns * buttonSize) / 2f;
        float yOffset = (getHeight() - client.rows * buttonSize) / 2f;

        List<MdSliderGroup> groups = client.getSliderGroups();
        for (int i = 0; i < groups.size(); i++) {
            MdSliderGroup group = groups.get(i);
            float left = xOffset + group.column * buttonSize + margin;
            float top = yOffset + group.row * buttonSize + margin;
            float width = buttonSize - (margin * 2f);
            float height = group.rowSpan * buttonSize - (margin * 2f);
            if (width <= 0 || height <= 0) {
                continue;
            }
            cell.set(left, top, left + width, top + height);
            drawGroupLabel(canvas, group);
        }

        List<MdSlider> sliders = client.getSliders();
        for (int i = 0; i < sliders.size(); i++) {
            MdSlider slider = sliders.get(i);
            if (!cellRect(slider, buttonSize, margin, xOffset, yOffset, cell)) {
                continue;
            }
            drawSlider(canvas, slider, radius);
        }

        drawBackButton(canvas);
    }

    /**
     * Back affordance for slider folders.
     *
     * A slider folder holds no buttons, so without this the only way out would be the
     * hardware back key - which some devices in this app's target range do not have.
     */
    private void drawBackButton(Canvas canvas) {
        if (!client.canGoBack()) {
            backButton.setEmpty();
            return;
        }

        float size = Math.min(Math.min(getWidth(), getHeight()) * 0.09f, 56f);
        float margin = size * 0.25f;
        backButton.set(margin, margin, margin + size, margin + size);

        fillPaint.setColor(BACK_BUTTON_BG);
        canvas.drawCircle(backButton.centerX(), backButton.centerY(), size / 2f, fillPaint);

        strokePaint.setColor(Color.WHITE);
        strokePaint.setStrokeWidth(Math.max(size * 0.09f, 2f));

        // A simple chevron pointing left
        float cx = backButton.centerX() + size * 0.06f;
        float cy = backButton.centerY();
        float arm = size * 0.20f;
        canvas.drawLine(cx + arm, cy - arm, cx - arm * 0.6f, cy, strokePaint);
        canvas.drawLine(cx - arm * 0.6f, cy, cx + arm, cy + arm, strokePaint);
    }

    /**
     * Band label, drawn rotated so a name like "Monitoring" fits the narrow column
     * the host reserves for it.
     */
    private void drawGroupLabel(Canvas canvas, MdSliderGroup group) {
        if (group.name == null || group.name.length() == 0) {
            return;
        }

        textPaint.setColor(Color.WHITE);
        textPaint.setTypeface(Typeface.DEFAULT_BOLD);
        textPaint.setTextAlign(Paint.Align.CENTER);
        textPaint.setTextSize(fitTextSize(group.name, cell.height() * 0.9f,
                Math.min(cell.width() * 0.42f, 30f)));

        float centerX = cell.centerX();
        float centerY = cell.centerY();

        canvas.save();
        canvas.rotate(-90f, centerX, centerY);
        canvas.drawText(group.name, centerX, centerY + textPaint.getTextSize() * 0.35f, textPaint);
        canvas.restore();
    }

    /**
     * Fills {@code out} with the on-screen area of a slider.
     *
     * @return false when the cell has no usable size.
     */
    private boolean cellRect(MdSlider slider, float buttonSize, float margin,
                             float xOffset, float yOffset, RectF out) {
        float left = xOffset + slider.column * buttonSize + margin;
        float top = yOffset + slider.row * buttonSize + margin;
        float width = slider.colSpan * buttonSize - (margin * 2f);
        float height = slider.rowSpan * buttonSize - (margin * 2f);
        if (width <= 0 || height <= 0) {
            return false;
        }
        out.set(left, top, left + width, top + height);
        return true;
    }

    private void drawSlider(Canvas canvas, MdSlider slider, float radius) {
        fillPaint.setColor(SLIDER_CARD);
        canvas.drawRoundRect(cell, radius, radius, fillPaint);

        float padding = Math.min(cell.width() * 0.08f, 10f);
        float titleHeight = Math.min(cell.height() * 0.11f, 34f);
        float valueHeight = Math.min(cell.height() * 0.09f, 28f);
        float muteHeight = slider.supportsMute ? Math.min(cell.height() * 0.12f, 40f) : 0f;

        int textColor = slider.muted ? SLIDER_DIM_TEXT : Color.WHITE;

        // Title
        textPaint.setColor(textColor);
        textPaint.setTypeface(Typeface.DEFAULT_BOLD);
        textPaint.setTextSize(fitTextSize(slider.displayName, cell.width() - padding * 2f,
                titleHeight * 0.72f));
        drawCenteredText(canvas, slider.displayName,
                cell.centerX(), cell.top + padding + titleHeight * 0.7f);

        // Track
        if (!layoutTrack(slider, track)) {
            return;
        }
        double shown = displayedValue(slider);
        float fillHeight = (float) (track.height() * shown);
        int fillColor = slider.muted ? SLIDER_MUTED_FILL : slider.accentColor();

        switch (slider.style) {
            case MdSlider.STYLE_CAPSULE:
                drawCapsuleTrack(canvas, slider, fillHeight, fillColor);
                break;
            case MdSlider.STYLE_KNOB:
                drawKnobTrack(canvas, slider, fillHeight, fillColor);
                break;
            default:
                drawClassicTrack(canvas, slider, fillHeight, fillColor);
                break;
        }

        // Percentage. Anchored to the card rather than to the track: the knob style
        // insets the track, and following it would put the text under the knob.
        textPaint.setColor(textColor);
        textPaint.setTypeface(Typeface.DEFAULT);
        textPaint.setTextSize(valueHeight * 0.78f);
        float valueBaseline = cell.bottom - padding - muteHeight - valueHeight * 0.2f;
        drawCenteredText(canvas, percentText(slider), cell.centerX(), valueBaseline);

        if (!slider.supportsMute) {
            return;
        }

        muteRect(slider, part);

        if (slider.style == MdSlider.STYLE_CAPSULE) {
            drawSpeakerIcon(canvas, slider, part);
            return;
        }

        // Mute button
        fillPaint.setColor(slider.muted ? SLIDER_MUTED_BG : SLIDER_MUTE_BG);
        float muteRadius = Math.min(part.height() / 3f, 8f);
        canvas.drawRoundRect(part, muteRadius, muteRadius, fillPaint);

        textPaint.setColor(Color.WHITE);
        textPaint.setTextSize(fitTextSize(slider.muted ? "Muted" : "Mute",
                part.width() * 0.8f, part.height() * 0.5f));
        drawCenteredText(canvas, slider.muted ? "Muted" : "Mute",
                part.centerX(), part.centerY() + textPaint.getTextSize() * 0.36f);
    }

    /** Narrow track with a small pill thumb. */
    private void drawClassicTrack(Canvas canvas, MdSlider slider, float fillHeight, int fillColor) {
        float trackRadius = track.width() / 2f;
        fillPaint.setColor(SLIDER_TRACK);
        canvas.drawRoundRect(track, trackRadius, trackRadius, fillPaint);

        if (fillHeight > 0) {
            part.set(track.left, track.bottom - fillHeight, track.right, track.bottom);
            fillPaint.setColor(fillColor);
            canvas.drawRoundRect(part, trackRadius, trackRadius, fillPaint);
        }

        float thumbHeight = Math.min(cell.height() * 0.07f, 22f);
        float thumbWidth = track.width() + Math.min(cell.width() * 0.22f, 22f);
        float thumbCenterY = track.bottom - fillHeight;
        part.set(cell.centerX() - thumbWidth / 2f, thumbCenterY - thumbHeight / 2f,
                cell.centerX() + thumbWidth / 2f, thumbCenterY + thumbHeight / 2f);
        fillPaint.setColor(slider.muted ? SLIDER_DIM_TEXT : Color.WHITE);
        canvas.drawRoundRect(part, thumbHeight / 2f, thumbHeight / 2f, fillPaint);
    }

    /**
     * Wide pill with no thumb; the fill is clipped to the pill so its rounded bottom
     * stays rounded while the top of the fill reads as a flat level line.
     */
    private void drawCapsuleTrack(Canvas canvas, MdSlider slider, float fillHeight, int fillColor) {
        float trackRadius = track.width() / 2f;

        fillPaint.setColor(SLIDER_TRACK);
        canvas.drawRoundRect(track, trackRadius, trackRadius, fillPaint);

        if (fillHeight > 0) {
            clipPath.reset();
            clipPath.addRoundRect(track, trackRadius, trackRadius, Path.Direction.CW);
            canvas.save();
            canvas.clipPath(clipPath);
            fillPaint.setColor(fillColor);
            canvas.drawRect(track.left, track.bottom - fillHeight, track.right, track.bottom, fillPaint);
            canvas.restore();
        }

        strokePaint.setColor(SLIDER_CAPSULE_EDGE);
        strokePaint.setStrokeWidth(Math.max(track.width() * 0.06f, 1.5f));
        canvas.drawRoundRect(track, trackRadius, trackRadius, strokePaint);
    }

    /** Wide track with a ring shaped knob riding on the fill level. */
    private void drawKnobTrack(Canvas canvas, MdSlider slider, float fillHeight, int fillColor) {
        float trackRadius = Math.max(track.width() * 0.3f, 4f);

        fillPaint.setColor(SLIDER_TRACK);
        canvas.drawRoundRect(track, trackRadius, trackRadius, fillPaint);

        if (fillHeight > 0) {
            part.set(track.left, track.bottom - fillHeight, track.right, track.bottom);
            fillPaint.setColor(fillColor);
            canvas.drawRoundRect(part, trackRadius, trackRadius, fillPaint);
        }

        float outer = track.width() * 0.85f;
        float centerY = track.bottom - fillHeight;
        fillPaint.setColor(slider.muted ? SLIDER_DIM_TEXT : Color.WHITE);
        canvas.drawCircle(cell.centerX(), centerY, outer, fillPaint);
        fillPaint.setColor(slider.muted ? SLIDER_MUTED_FILL : SLIDER_CARD);
        canvas.drawCircle(cell.centerX(), centerY, outer * 0.55f, fillPaint);
    }

    /** Speaker glyph used as the mute toggle in the capsule style. */
    private void drawSpeakerIcon(Canvas canvas, MdSlider slider, RectF area) {
        float size = Math.min(area.height(), 26f);
        float centerX = area.centerX();
        float centerY = area.centerY();
        int color = slider.muted ? SLIDER_MUTED_BG : Color.WHITE;

        fillPaint.setColor(color);
        strokePaint.setColor(color);
        strokePaint.setStrokeWidth(Math.max(size * 0.09f, 1.6f));

        float boxWidth = size * 0.20f;
        float boxHeight = size * 0.34f;
        float left = centerX - size * 0.42f;
        canvas.drawRect(left, centerY - boxHeight / 2f, left + boxWidth, centerY + boxHeight / 2f, fillPaint);

        clipPath.reset();
        clipPath.moveTo(left + boxWidth, centerY - boxHeight / 2f);
        clipPath.lineTo(left + boxWidth + size * 0.26f, centerY - size * 0.38f);
        clipPath.lineTo(left + boxWidth + size * 0.26f, centerY + size * 0.38f);
        clipPath.lineTo(left + boxWidth, centerY + boxHeight / 2f);
        clipPath.close();
        canvas.drawPath(clipPath, fillPaint);

        float waveX = centerX + size * 0.04f;
        if (slider.muted) {
            canvas.drawLine(waveX, centerY - size * 0.24f, waveX + size * 0.32f, centerY + size * 0.24f, strokePaint);
            canvas.drawLine(waveX, centerY + size * 0.24f, waveX + size * 0.32f, centerY - size * 0.24f, strokePaint);
            return;
        }

        part.set(waveX, centerY - size * 0.24f, waveX + size * 0.24f, centerY + size * 0.24f);
        canvas.drawArc(part, -60, 120, false, strokePaint);
        part.set(waveX, centerY - size * 0.40f, waveX + size * 0.44f, centerY + size * 0.40f);
        canvas.drawArc(part, -60, 120, false, strokePaint);
    }

    /**
     * Fills {@code out} with the track area, assuming {@link #cell} is set.
     * Shared by the drawing and the touch code so a drag can never address a
     * different strip than the one on screen.
     *
     * @return false when the cell is too short for a usable track.
     */
    private boolean layoutTrack(MdSlider slider, RectF out) {
        float padding = Math.min(cell.width() * 0.08f, 10f);
        float titleHeight = Math.min(cell.height() * 0.11f, 34f);
        float valueHeight = Math.min(cell.height() * 0.09f, 28f);
        float muteHeight = slider.supportsMute ? Math.min(cell.height() * 0.12f, 40f) : 0f;

        float top = cell.top + padding + titleHeight;
        float bottom = cell.bottom - padding - valueHeight - muteHeight;

        float width;
        switch (slider.style) {
            case MdSlider.STYLE_CAPSULE:
                width = Math.max(Math.min(cell.width() * 0.46f, 52f), 18f);
                break;
            case MdSlider.STYLE_KNOB:
                width = Math.max(Math.min(cell.width() * 0.36f, 40f), 14f);
                // The knob is centred on the top of the fill, so at the extremes half
                // of it would spill past the track and cover the title or the
                // percentage. Inset both ends by its radius.
                top += width * 0.85f;
                bottom -= width * 0.85f;
                break;
            default:
                width = Math.min(cell.width() * 0.16f, 16f);
                break;
        }

        if (bottom - top < width) {
            return false;
        }

        float left = cell.centerX() - width / 2f;
        out.set(left, top, left + width, bottom);
        return true;
    }

    /** Fills {@code out} with the mute button area, assuming {@link #cell} is set. */
    private void muteRect(MdSlider slider, RectF out) {
        float padding = Math.min(cell.width() * 0.08f, 10f);
        float muteHeight = Math.min(cell.height() * 0.12f, 40f);
        out.set(cell.left + padding, cell.bottom - padding - muteHeight,
                cell.right - padding, cell.bottom - padding);
    }

    /** The dragged value wins over the model so the thumb tracks the finger. */
    private double displayedValue(MdSlider slider) {
        return MdSlider.clamp(slider.value);
    }

    private String percentText(MdSlider slider) {
        return slider.percent() + "%";
    }

    /** Shrinks the text until it fits the available width, down to 8px. */
    private float fitTextSize(String text, float maxWidth, float preferred) {
        if (text == null || text.length() == 0 || maxWidth <= 0) {
            return Math.max(preferred, 8f);
        }
        float size = Math.max(preferred, 8f);
        textPaint.setTextSize(size);
        while (size > 8f && textPaint.measureText(text) > maxWidth) {
            size -= 1f;
            textPaint.setTextSize(size);
        }
        return size;
    }

    private void drawCenteredText(Canvas canvas, String text, float centerX, float baselineY) {
        if (text == null || text.length() == 0) {
            return;
        }
        textPaint.setTextAlign(Paint.Align.CENTER);
        canvas.drawText(text, centerX, baselineY, textPaint);
    }

    private void drawButton(Canvas canvas, MdButton button, float radius) {
        if (client.buttonBackground) {
            fillPaint.setColor(parseColor(button == null ? null : button.backgroundColorHex));
            canvas.drawRoundRect(rect, radius, radius, fillPaint);
        }

        if (button == null) {
            return;
        }

        // The web client stacks background < icon < label, each stretched to fill
        // the button (CSS object-fit: fill).
        drawLayer(canvas, button.getIcon(), button.getIconShader(), radius);
        drawLayer(canvas, button.getLabel(), button.getLabelShader(), radius);
    }

    private void drawLayer(Canvas canvas, Bitmap bitmap, BitmapShader shader, float radius) {
        if (bitmap == null || shader == null || bitmap.isRecycled()) {
            return;
        }
        if (bitmap.getWidth() == 0 || bitmap.getHeight() == 0) {
            return;
        }

        shaderMatrix.reset();
        shaderMatrix.setScale(rect.width() / bitmap.getWidth(), rect.height() / bitmap.getHeight());
        shaderMatrix.postTranslate(rect.left, rect.top);
        shader.setLocalMatrix(shaderMatrix);

        imagePaint.setShader(shader);
        canvas.drawRoundRect(rect, radius, radius, imagePaint);
        imagePaint.setShader(null);
    }

    private static int parseColor(String hex) {
        if (hex == null || hex.length() == 0) {
            return EMPTY_BACKGROUND;
        }
        try {
            return Color.parseColor(hex.startsWith("#") ? hex : "#" + hex);
        } catch (IllegalArgumentException e) {
            return EMPTY_BACKGROUND;
        }
    }

    // ---------------------------------------------------------------- touch

    @Override
    public boolean onTouchEvent(MotionEvent event) {
        if (client == null || !client.isDeckReady()) {
            return false;
        }

        if (client.hasSliders()) {
            return onSliderTouch(event);
        }

        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN: {
                int column = columnAt(event.getX());
                int row = rowAt(event.getY());
                if (column < 0 || row < 0 || client.getButton(column, row) == null) {
                    // Empty cells carry no handlers in the web client either.
                    return false;
                }
                pressedX = column;
                pressedY = row;
                longPressFired = false;
                emit("BUTTON_PRESS");
                handler.postDelayed(longPressRunnable, longPressDelay);
                invalidate();
                return true;
            }
            case MotionEvent.ACTION_MOVE: {
                if (pressedX < 0) {
                    return false;
                }
                // Sliding off the button releases it, matching (pointerleave).
                if (columnAt(event.getX()) != pressedX || rowAt(event.getY()) != pressedY) {
                    release();
                }
                return true;
            }
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_CANCEL: {
                if (pressedX < 0) {
                    return false;
                }
                release();
                if (event.getActionMasked() == MotionEvent.ACTION_UP) {
                    // Keeps accessibility services aware of the interaction.
                    performClick();
                }
                return true;
            }
            default:
                return false;
        }
    }

    /**
     * Touch handling for a slider folder.
     *
     * A tap on the mute button toggles mute; anywhere else in the card starts a
     * drag that follows the finger, exactly like the web client's pointerdown on
     * the whole track. Intermediate values are throttled so a drag does not
     * flood the socket on a 2013 radio.
     */
    private boolean onSliderTouch(MotionEvent event) {
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN: {
                if (!backButton.isEmpty() && backButton.contains(event.getX(), event.getY())) {
                    if (interactionListener != null) {
                        interactionListener.onBack();
                    }
                    return true;
                }

                MdSlider slider = sliderAt(event.getX(), event.getY());
                if (slider == null) {
                    return false;
                }

                // cell is left set to this slider's area by sliderAt().
                if (slider.supportsMute) {
                    muteRect(slider, part);
                    if (part.contains(event.getX(), event.getY())) {
                        boolean muted = !slider.muted;
                        slider.muted = muted;
                        if (interactionListener != null) {
                            interactionListener.onSliderMute(slider, muted);
                        }
                        invalidate();
                        return true;
                    }
                }

                draggingSlider = slider;
                lastSentValue = -1;
                applyDrag(event.getY(), false);
                return true;
            }
            case MotionEvent.ACTION_MOVE: {
                if (draggingSlider == null) {
                    return false;
                }
                applyDrag(event.getY(), false);
                return true;
            }
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_CANCEL: {
                if (draggingSlider == null) {
                    return false;
                }
                applyDrag(event.getY(), true);
                draggingSlider = null;
                lastSentValue = -1;
                if (event.getActionMasked() == MotionEvent.ACTION_UP) {
                    performClick();
                }
                return true;
            }
            default:
                return false;
        }
    }

    /**
     * Maps a finger position onto the dragged slider and pushes the value.
     *
     * @param force send even when the value barely moved (used on release)
     */
    private void applyDrag(float y, boolean force) {
        MdSlider slider = draggingSlider;
        if (slider == null || !placeCell(slider) || !layoutTrack(slider, track)) {
            return;
        }

        double value = (track.bottom - y) / track.height();
        value = Math.round(MdSlider.clamp(value) * 100.0) / 100.0;

        slider.value = value;
        invalidate();

        if (!force && lastSentValue >= 0 && Math.abs(value - lastSentValue) < SLIDER_SEND_STEP) {
            return;
        }

        lastSentValue = value;
        if (interactionListener != null) {
            interactionListener.onSliderChange(slider, value);
        }
    }

    /**
     * Finds the slider under a point, leaving {@link #cell} set to its area.
     *
     * @return null when the point is not on a slider.
     */
    private MdSlider sliderAt(float x, float y) {
        List<MdSlider> sliders = client.getSliders();
        for (int i = 0; i < sliders.size(); i++) {
            MdSlider slider = sliders.get(i);
            if (placeCell(slider) && cell.contains(x, y)) {
                return slider;
            }
        }
        return null;
    }

    /** Sets {@link #cell} to the on-screen area of a slider. */
    private boolean placeCell(MdSlider slider) {
        float buttonSize = buttonSize();
        if (buttonSize <= 0) {
            return false;
        }
        float margin = (float) ((client.buttonSpacing / 100.0) * buttonSize) / 2f;
        float xOffset = (getWidth() - client.columns * buttonSize) / 2f;
        float yOffset = (getHeight() - client.rows * buttonSize) / 2f;
        return cellRect(slider, buttonSize, margin, xOffset, yOffset, cell);
    }

    @Override
    public boolean performClick() {
        return super.performClick();
    }

    private void release() {
        handler.removeCallbacks(longPressRunnable);
        emit(longPressFired ? "BUTTON_LONG_PRESS_RELEASE" : "BUTTON_RELEASE");
        pressedX = -1;
        pressedY = -1;
        longPressFired = false;
        invalidate();
    }

    private void emit(String method) {
        if (interactionListener != null && pressedX >= 0 && pressedY >= 0) {
            interactionListener.onInteraction(method, pressedX, pressedY);
        }
    }

    /** @return the grid column under x, or -1 if x falls outside the grid. */
    private int columnAt(float x) {
        float buttonSize = buttonSize();
        if (buttonSize <= 0) {
            return -1;
        }
        float xOffset = (getWidth() - client.columns * buttonSize) / 2f;
        int column = (int) Math.floor((x - xOffset) / buttonSize);
        return (column < 0 || column >= client.columns) ? -1 : column;
    }

    /** @return the grid row under y, or -1 if y falls outside the grid. */
    private int rowAt(float y) {
        float buttonSize = buttonSize();
        if (buttonSize <= 0) {
            return -1;
        }
        float yOffset = (getHeight() - client.rows * buttonSize) / 2f;
        int row = (int) Math.floor((y - yOffset) / buttonSize);
        return (row < 0 || row >= client.rows) ? -1 : row;
    }

    private float buttonSize() {
        if (client == null || client.rows <= 0 || client.columns <= 0) {
            return 0;
        }
        return Math.min((float) getWidth() / client.columns, (float) getHeight() / client.rows);
    }
}
