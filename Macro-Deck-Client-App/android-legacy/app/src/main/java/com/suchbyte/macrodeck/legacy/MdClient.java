package com.suchbyte.macrodeck.legacy;

import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.net.URI;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Macro Deck protocol 2, ported from the Ionic client
 * (src/app/services/protocol/protocol2.service.ts and websocket.service.ts).
 *
 * Flow: connect -> send CONNECTED -> host accepts -> host pushes GET_CONFIG ->
 * we answer with GET_BUTTONS -> host pushes GET_BUTTONS / UPDATE_BUTTON /
 * UPDATE_LABEL from then on. Presses travel the other way as BUTTON_*.
 *
 * A slider folder answers the same GET_BUTTONS request with GET_SLIDERS instead,
 * so the deck holds either buttons or sliders, never both. Slider moves travel
 * back as SLIDER_CHANGE / SLIDER_MUTE, addressed by SliderId.
 */
public class MdClient implements Ws.Listener {

    private static final String TAG = "MdClient";

    /** Protocol revision the host negotiates against; must stay "20". */
    private static final String API_VERSION = "20";

    public interface Listener {
        /** Socket is up; the host has not accepted the client yet. */
        void onSocketOpen();

        /** First GET_CONFIG arrived: the host accepted us and the deck is live. */
        void onDeckReady();

        void onConfigChanged(int rows, int columns, double buttonSpacing,
                             double buttonRadius, boolean buttonBackground);

        void onButtonsChanged();

        /** The host sent a page of the soundboard folder (see {@link #soundNames}). */
        void onSoundboardChanged();

        void onDisconnected(int code, String reason, boolean clean);
    }

    private final Conn conn;
    private final String clientId;
    private final boolean skipSslValidation;
    private final Listener listener;

    private final Map<String, MdButton> buttons = new HashMap<String, MdButton>();

    /** Populated instead of {@link #buttons} while the host serves a slider folder. */
    private final List<MdSlider> sliders = new ArrayList<MdSlider>();

    /** Band labels that go with {@link #sliders}; empty when the host sends none. */
    private final List<MdSliderGroup> sliderGroups = new ArrayList<MdSliderGroup>();

    /** Soundboard folder: names of the sounds of the current page, played on the host PC. */
    public final List<String> soundNames = new ArrayList<String>();
    public int soundsPage = 1;
    public String soundQuery = "";
    public boolean soundsHasMore;
    public int soundRows = 4;
    public int soundColumns = 6;

    /** true while the host serves a soundboard folder instead of buttons or sliders. */
    private boolean soundboardMode;

    private Ws ws;
    private boolean deckReady;
    private boolean closedByUs;

    /** Whether the folder we are in has a parent to navigate back to. */
    private boolean canGoBack;

    public int rows = 3;
    public int columns = 5;
    public double buttonSpacing = 10;
    public double buttonRadius = 40;
    public boolean buttonBackground = true;

    public MdClient(Conn conn, String clientId, boolean skipSslValidation, Listener listener) {
        this.conn = conn;
        this.clientId = clientId;
        this.skipSslValidation = skipSslValidation;
        this.listener = listener;
    }

    public void connect() {
        deckReady = false;
        closedByUs = false;
        try {
            ws = new Ws(new URI(conn.url()), skipSslValidation, this);
            ws.connect();
        } catch (Exception e) {
            listener.onDisconnected(1006, "Invalid address: " + conn.url(), false);
        }
    }

    public void disconnect() {
        closedByUs = true;
        if (ws != null) {
            ws.close();
        }
    }

    public boolean isDeckReady() {
        return deckReady;
    }

    public MdButton getButton(int x, int y) {
        return buttons.get(key(x, y));
    }

    /** true = the current folder is a slider folder, so the deck draws sliders. */
    public boolean isSoundboard() {
        return soundboardMode;
    }

    public boolean hasSliders() {
        return !sliders.isEmpty();
    }

    public List<MdSlider> getSliders() {
        return sliders;
    }

    public List<MdSliderGroup> getSliderGroups() {
        return sliderGroups;
    }

    public boolean canGoBack() {
        return canGoBack;
    }

    public MdSlider getSlider(String sliderId) {
        for (int i = 0; i < sliders.size(); i++) {
            MdSlider slider = sliders.get(i);
            if (slider.sliderId.equals(sliderId)) {
                return slider;
            }
        }
        return null;
    }

    // ------------------------------------------------------------- outgoing

    /**
     * @param type one of BUTTON_PRESS, BUTTON_RELEASE, BUTTON_LONG_PRESS,
     *             BUTTON_LONG_PRESS_RELEASE
     */
    public void sendInteraction(String type, int x, int y) {
        try {
            JSONObject message = new JSONObject();
            message.put("Method", type);
            // The web client addresses buttons as "row_column", i.e. Y then X.
            message.put("Message", y + "_" + x);
            send(message);
        } catch (Exception e) {
            Log.w(TAG, "Could not send interaction", e);
        }
    }

    /**
     * Asks the host to move us to the parent folder.
     *
     * A slider folder has no buttons, so this is the only way out of one.
     */
    public void sendFolderBack() {
        try {
            JSONObject message = new JSONObject();
            message.put("Method", "FOLDER_BACK");
            send(message);
        } catch (Exception e) {
            Log.w(TAG, "Could not send folder back", e);
        }
    }

    /** Sends a new value (0.0 - 1.0) for a slider. */
    public void sendSliderChange(MdSlider slider, double value) {
        try {
            JSONObject message = new JSONObject();
            message.put("Method", "SLIDER_CHANGE");
            message.put("SliderId", slider.sliderId);
            message.put("ProviderId", slider.providerId);
            message.put("Value", MdSlider.clamp(value));
            send(message);
        } catch (Exception e) {
            Log.w(TAG, "Could not send slider change", e);
        }
    }

    /** Mutes or unmutes a slider. */
    public void sendSliderMute(MdSlider slider, boolean muted) {
        try {
            JSONObject message = new JSONObject();
            message.put("Method", "SLIDER_MUTE");
            message.put("SliderId", slider.sliderId);
            message.put("ProviderId", slider.providerId);
            message.put("Muted", muted);
            send(message);
        } catch (Exception e) {
            Log.w(TAG, "Could not send slider mute", e);
        }
    }

    private void send(JSONObject message) {
        if (ws != null && ws.isOpen()) {
            ws.send(message.toString());
        }
    }

    // ------------------------------------------------------------- incoming

    @Override
    public void onOpen() {
        listener.onSocketOpen();
        try {
            JSONObject connected = new JSONObject();
            connected.put("Method", "CONNECTED");
            connected.put("Client-Id", clientId);
            connected.put("API", API_VERSION);
            // The host keys some behaviour off this string; "Web" is the value
            // the official client sends and the one hosts are known to accept.
            connected.put("Device-Type", "Web");
            // Tells the host that this client draws a soundboard folder itself
            connected.put("Soundboard", true);
            if (conn.token != null && conn.token.length() > 0) {
                connected.put("Token", conn.token);
            }
            send(connected);
        } catch (Exception e) {
            Log.w(TAG, "Could not send CONNECTED", e);
        }
    }

    @Override
    public void onText(String text) {
        try {
            JSONObject message = new JSONObject(text);
            String method = message.optString("Method", "");
            if (method.length() == 0) {
                return;
            }

            if (method.equals("GET_CONFIG")) {
                handleConfig(message);
            } else if (method.equals("GET_BUTTONS")) {
                if (!deckReady) {
                    return;
                }
                handleButtons(message);
            } else if (method.equals("UPDATE_BUTTON")) {
                if (!deckReady) {
                    return;
                }
                handleUpdateButton(message);
            } else if (method.equals("UPDATE_LABEL")) {
                if (!deckReady) {
                    return;
                }
                handleUpdateLabel(message);
            } else if (method.equals("GET_SLIDERS")) {
                if (!deckReady) {
                    return;
                }
                handleSliders(message);
            } else if (method.equals("GET_SOUNDBOARD")) {
                if (!deckReady) {
                    return;
                }
                handleSoundboard(message);
            } else if (method.equals("UPDATE_SLIDER")) {
                if (!deckReady) {
                    return;
                }
                handleUpdateSlider(message);
            }
        } catch (Exception e) {
            Log.w(TAG, "Could not handle message", e);
        }
    }

    private void handleConfig(JSONObject message) {
        rows = message.optInt("Rows", rows);
        columns = message.optInt("Columns", columns);
        buttonSpacing = message.optDouble("ButtonSpacing", buttonSpacing);
        buttonRadius = message.optDouble("ButtonRadius", buttonRadius);
        buttonBackground = message.optBoolean("ButtonBackground", buttonBackground);

        listener.onConfigChanged(rows, columns, buttonSpacing, buttonRadius, buttonBackground);

        if (!deckReady) {
            deckReady = true;
            listener.onDeckReady();
        }

        // The config can change the grid size, so always re-pull the buttons.
        try {
            JSONObject request = new JSONObject();
            request.put("Method", "GET_BUTTONS");
            send(request);
        } catch (Exception e) {
            Log.w(TAG, "Could not request buttons", e);
        }
    }

    private void handleSoundboard(JSONObject message) {
        // Buttons, sliders and the soundboard are mutually exclusive
        sliders.clear();
        sliderGroups.clear();
        buttons.clear();
        canGoBack = false;
        soundboardMode = true;

        soundsPage = message.optInt("Page", 1);
        soundQuery = message.optString("Query", "");
        soundsHasMore = message.optBoolean("HasMore", false);
        soundRows = Math.max(1, message.optInt("Rows", soundRows));
        soundColumns = Math.max(1, message.optInt("Columns", soundColumns));

        soundNames.clear();
        JSONArray incoming = message.optJSONArray("Sounds");
        if (incoming != null) {
            for (int i = 0; i < incoming.length(); i++) {
                JSONObject sound = incoming.optJSONObject(i);
                soundNames.add(sound == null ? "" : sound.optString("Name", ""));
            }
        }
        listener.onSoundboardChanged();
    }

    /** Searches the whole MyInstants site; an empty query goes back to the trending sounds. */
    public void sendSoundboardSearch(String query) {
        try {
            JSONObject message = new JSONObject();
            message.put("Method", "SOUNDBOARD_ACTION");
            message.put("Action", "SEARCH");
            message.put("Query", query == null ? "" : query);
            send(message);
        } catch (Exception e) {
            Log.w(TAG, "Could not send soundboard search", e);
        }
    }

    /** Sends what the user did in the soundboard: PLAY (with the index), PREVIOUS, NEXT, STOP, BACK, ... */
    public void sendSoundboardAction(String action, int index) {
        try {
            JSONObject message = new JSONObject();
            message.put("Method", "SOUNDBOARD_ACTION");
            message.put("Action", action);
            message.put("Index", index);
            send(message);
        } catch (Exception e) {
            Log.w(TAG, "Could not send soundboard action", e);
        }
    }

    private void handleButtons(JSONObject message) {
        JSONArray incoming = message.optJSONArray("Buttons");
        if (incoming == null) {
            return;
        }

        // Buttons and sliders are mutually exclusive: arriving here means the
        // client left a slider folder.
        soundboardMode = false;
        sliders.clear();
        sliderGroups.clear();
        canGoBack = false;

        for (MdButton button : buttons.values()) {
            button.release();
        }
        buttons.clear();

        for (int i = 0; i < incoming.length(); i++) {
            JSONObject raw = incoming.optJSONObject(i);
            if (raw != null) {
                MdButton button = apply(raw);
                buttons.put(key(button.x, button.y), button);
            }
        }
        listener.onButtonsChanged();
    }

    private void handleUpdateButton(JSONObject message) {
        JSONArray incoming = message.optJSONArray("Buttons");
        if (incoming == null || incoming.length() == 0) {
            return;
        }
        JSONObject raw = incoming.optJSONObject(0);
        if (raw == null) {
            return;
        }
        MdButton button = apply(raw);
        buttons.put(key(button.x, button.y), button);
        listener.onButtonsChanged();
    }

    private void handleSliders(JSONObject message) {
        JSONArray incoming = message.optJSONArray("Sliders");
        if (incoming == null) {
            return;
        }

        // Entering a slider folder: the buttons of the previous folder are gone.
        for (MdButton button : buttons.values()) {
            button.release();
        }
        buttons.clear();
        soundboardMode = false;
        sliders.clear();
        sliderGroups.clear();
        canGoBack = message.optBoolean("CanGoBack", false);

        for (int i = 0; i < incoming.length(); i++) {
            JSONObject raw = incoming.optJSONObject(i);
            if (raw != null) {
                sliders.add(MdSlider.fromJson(raw));
            }
        }

        JSONArray groups = message.optJSONArray("Groups");
        if (groups != null) {
            for (int i = 0; i < groups.length(); i++) {
                JSONObject raw = groups.optJSONObject(i);
                if (raw != null) {
                    sliderGroups.add(MdSliderGroup.fromJson(raw));
                }
            }
        }

        listener.onButtonsChanged();
    }

    /** UPDATE_SLIDER carries the current state of a single slider. */
    private void handleUpdateSlider(JSONObject message) {
        JSONArray incoming = message.optJSONArray("Sliders");
        if (incoming == null || incoming.length() == 0) {
            return;
        }
        JSONObject raw = incoming.optJSONObject(0);
        if (raw == null) {
            return;
        }

        MdSlider updated = MdSlider.fromJson(raw);
        for (int i = 0; i < sliders.size(); i++) {
            if (sliders.get(i).sliderId.equals(updated.sliderId)) {
                sliders.set(i, updated);
                listener.onButtonsChanged();
                return;
            }
        }

        // An id we have never seen means our set is stale; take it as-is.
        sliders.add(updated);
        listener.onButtonsChanged();
    }

    /** UPDATE_LABEL touches only the label of a button that already exists. */
    private void handleUpdateLabel(JSONObject message) {
        JSONArray incoming = message.optJSONArray("Buttons");
        if (incoming == null || incoming.length() == 0) {
            return;
        }
        JSONObject raw = incoming.optJSONObject(0);
        if (raw == null) {
            return;
        }
        int x = raw.optInt("Position_X", -1);
        int y = raw.optInt("Position_Y", -1);
        MdButton existing = buttons.get(key(x, y));
        if (existing == null) {
            return;
        }
        existing.setLabelBase64(optString(raw, "LabelBase64"));
        listener.onButtonsChanged();
    }

    /** Writes an incoming Protocol2Button onto the cached button for its cell. */
    private MdButton apply(JSONObject raw) {
        int x = raw.optInt("Position_X", 0);
        int y = raw.optInt("Position_Y", 0);

        MdButton button = buttons.get(key(x, y));
        if (button == null) {
            button = new MdButton(x, y);
        }
        button.backgroundColorHex = optString(raw, "BackgroundColorHex");
        button.setIconBase64(optString(raw, "IconBase64"));
        button.setLabelBase64(optString(raw, "LabelBase64"));
        return button;
    }

    /** optString() returns the literal string "null" for JSON nulls; this does not. */
    private static String optString(JSONObject json, String name) {
        if (json.isNull(name)) {
            return null;
        }
        return json.optString(name, null);
    }

    @Override
    public void onClose(int code, String reason) {
        boolean clean = closedByUs || code == 1000;
        listener.onDisconnected(code, reason, clean);
    }

    public void releaseBitmaps() {
        for (MdButton button : buttons.values()) {
            button.release();
        }
        buttons.clear();
        sliders.clear();
        sliderGroups.clear();
    }

    private static String key(int x, int y) {
        return x + "_" + y;
    }
}
