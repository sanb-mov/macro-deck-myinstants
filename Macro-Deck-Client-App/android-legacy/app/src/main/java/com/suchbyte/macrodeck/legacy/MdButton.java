package com.suchbyte.macrodeck.legacy;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.BitmapShader;
import android.graphics.Shader;
import android.util.Base64;

/**
 * One deck button, as delivered by protocol 2
 * (src/app/datatypes/protocol2/protocol2-button.ts).
 *
 * Both the icon and the label arrive as base64 images that the web client
 * layers on top of the background colour, so this holds the decoded bitmaps and
 * the base64 they came from, to skip re-decoding when nothing changed.
 */
public class MdButton {

    public int x;
    public int y;
    public String backgroundColorHex;

    private String iconBase64;
    private String labelBase64;

    private Bitmap icon;
    private Bitmap label;

    // The deck draws every image clipped to the button's rounded corners, which
    // needs a shader rather than a plain drawBitmap. Cached here so a press,
    // which repaints the grid, does not reallocate one per frame.
    private BitmapShader iconShader;
    private BitmapShader labelShader;

    public MdButton(int x, int y) {
        this.x = x;
        this.y = y;
    }

    public Bitmap getIcon() {
        return icon;
    }

    public Bitmap getLabel() {
        return label;
    }

    public BitmapShader getIconShader() {
        if (iconShader == null && icon != null) {
            iconShader = new BitmapShader(icon, Shader.TileMode.CLAMP, Shader.TileMode.CLAMP);
        }
        return iconShader;
    }

    public BitmapShader getLabelShader() {
        if (labelShader == null && label != null) {
            labelShader = new BitmapShader(label, Shader.TileMode.CLAMP, Shader.TileMode.CLAMP);
        }
        return labelShader;
    }

    public void setIconBase64(String base64) {
        if (same(iconBase64, base64)) {
            return;
        }
        iconBase64 = base64;
        recycle(icon);
        icon = decode(base64);
        iconShader = null;
    }

    public void setLabelBase64(String base64) {
        if (same(labelBase64, base64)) {
            return;
        }
        labelBase64 = base64;
        recycle(label);
        label = decode(base64);
        labelShader = null;
    }

    public void release() {
        recycle(icon);
        recycle(label);
        icon = null;
        label = null;
        iconShader = null;
        labelShader = null;
    }

    private static boolean same(String a, String b) {
        return a == null ? b == null : a.equals(b);
    }

    private static void recycle(Bitmap bitmap) {
        if (bitmap != null && !bitmap.isRecycled()) {
            bitmap.recycle();
        }
    }

    /**
     * Decoding is best-effort: on a 2013 device the heap is small, and a deck
     * full of large icons can genuinely run out of memory. A button that fails
     * to decode simply renders without its image instead of taking the app down.
     */
    private static Bitmap decode(String base64) {
        if (base64 == null || base64.length() == 0) {
            return null;
        }
        try {
            // Macro Deck sends the raw base64, but tolerate a data: URI prefix.
            int comma = base64.indexOf(',');
            if (base64.startsWith("data:") && comma > 0) {
                base64 = base64.substring(comma + 1);
            }
            byte[] bytes = Base64.decode(base64, Base64.DEFAULT);
            return BitmapFactory.decodeByteArray(bytes, 0, bytes.length);
        } catch (Throwable ignored) {
            return null;
        }
    }
}
