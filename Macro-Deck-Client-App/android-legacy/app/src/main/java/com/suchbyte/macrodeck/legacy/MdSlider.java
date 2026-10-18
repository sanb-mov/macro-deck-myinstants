package com.suchbyte.macrodeck.legacy;

import android.graphics.Color;

import org.json.JSONObject;

/**
 * One slider, as delivered by protocol 2 (GET_SLIDERS / UPDATE_SLIDER).
 *
 * Unlike a button, a slider is addressed by its id rather than by grid cell:
 * the host places it on a cell for layout, but interactions carry SliderId so a
 * re-layout cannot send a value to the wrong channel.
 */
public class MdSlider {

    /** Accent colour used when the host sends none or an unparsable one. */
    private static final int DEFAULT_COLOR = 0xFF0067CD;

    public String sliderId = "";
    public String providerId = "";
    public String displayName = "";
    public int position;

    /** 0.0 - 1.0 */
    public double value;
    public boolean muted;
    public boolean supportsMute = true;

    public String colorHex;

    /** Empty when the host does not group sliders (classic mode). */
    public String group = "";

    /** Visual style, matching SuchByte.MacroDeck.Sliders.SliderStyle on the host. */
    public int style = STYLE_CLASSIC;

    public static final int STYLE_CLASSIC = 0;
    public static final int STYLE_CAPSULE = 1;
    public static final int STYLE_KNOB = 2;

    // Grid placement chosen by the host.
    public int row;
    public int column;
    public int rowSpan = 1;
    public int colSpan = 1;

    public static MdSlider fromJson(JSONObject raw) {
        MdSlider slider = new MdSlider();
        slider.sliderId = optString(raw, "SliderId", "");
        slider.providerId = optString(raw, "ProviderId", "");
        slider.displayName = optString(raw, "DisplayName", "");
        slider.position = raw.optInt("Position", 0);
        slider.value = clamp(raw.optDouble("Value", 0));
        slider.muted = raw.optBoolean("Muted", false);
        slider.supportsMute = raw.optBoolean("SupportsMute", true);
        slider.colorHex = optString(raw, "ColorHex", null);
        slider.group = optString(raw, "Group", "");
        slider.row = raw.optInt("Row", 0);
        slider.column = raw.optInt("Column", 0);
        slider.rowSpan = Math.max(raw.optInt("RowSpan", 1), 1);
        slider.colSpan = Math.max(raw.optInt("ColSpan", 1), 1);
        slider.style = raw.optInt("Style", STYLE_CLASSIC);
        return slider;
    }

    public int accentColor() {
        if (colorHex == null || colorHex.length() == 0) {
            return DEFAULT_COLOR;
        }
        try {
            return Color.parseColor(colorHex.startsWith("#") ? colorHex : "#" + colorHex);
        } catch (IllegalArgumentException e) {
            return DEFAULT_COLOR;
        }
    }

    public int percent() {
        return (int) Math.round(clamp(value) * 100.0);
    }

    public static double clamp(double value) {
        if (value < 0) {
            return 0;
        }
        if (value > 1) {
            return 1;
        }
        return value;
    }

    /** optString() returns the literal string "null" for JSON nulls; this does not. */
    private static String optString(JSONObject json, String name, String fallback) {
        if (json.isNull(name)) {
            return fallback;
        }
        return json.optString(name, fallback);
    }
}
