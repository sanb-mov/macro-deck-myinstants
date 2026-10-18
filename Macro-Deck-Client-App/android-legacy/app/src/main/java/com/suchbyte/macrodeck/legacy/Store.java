package com.suchbyte.macrodeck.legacy;

import android.content.Context;
import android.content.SharedPreferences;

import org.json.JSONArray;

import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

/**
 * Persistence layer, backed by SharedPreferences. The Ionic client uses
 * @ionic/storage for the same job (src/app/services/settings/settings.service.ts);
 * the keys and defaults here are kept identical in meaning.
 */
public class Store {

    private static final String PREFS = "macro_deck_legacy";

    private static final String KEY_CONNECTIONS = "connections";
    private static final String KEY_CLIENT_ID = "client_id";
    private static final String KEY_LONG_PRESS_DELAY = "button_long_press_delay";
    private static final String KEY_SKIP_SSL = "skip_ssl_validation";
    private static final String KEY_KEEP_AWAKE = "wake_lock_enabled";

    private final SharedPreferences prefs;

    public Store(Context context) {
        this.prefs = context.getApplicationContext().getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    // --------------------------------------------------------- connections

    public List<Conn> getConnections() {
        List<Conn> connections = new ArrayList<Conn>();
        String raw = prefs.getString(KEY_CONNECTIONS, "[]");
        try {
            JSONArray array = new JSONArray(raw);
            for (int i = 0; i < array.length(); i++) {
                connections.add(Conn.fromJson(array.getJSONObject(i)));
            }
        } catch (Exception ignored) {
            // Corrupted store: start from an empty list rather than crashing.
        }
        return connections;
    }

    public void saveConnections(List<Conn> connections) {
        JSONArray array = new JSONArray();
        try {
            for (Conn conn : connections) {
                array.put(conn.toJson());
            }
        } catch (Exception ignored) {
        }
        prefs.edit().putString(KEY_CONNECTIONS, array.toString()).apply();
    }

    /** Adds the connection, or replaces the existing one with the same id. */
    public void upsert(Conn conn) {
        List<Conn> connections = getConnections();
        boolean replaced = false;
        for (int i = 0; i < connections.size(); i++) {
            if (connections.get(i).id.equals(conn.id)) {
                connections.set(i, conn);
                replaced = true;
                break;
            }
        }
        if (!replaced) {
            connections.add(conn);
        }
        saveConnections(connections);
    }

    public void delete(String id) {
        List<Conn> connections = getConnections();
        for (int i = 0; i < connections.size(); i++) {
            if (connections.get(i).id.equals(id)) {
                connections.remove(i);
                break;
            }
        }
        saveConnections(connections);
    }

    public Conn find(String id) {
        for (Conn conn : getConnections()) {
            if (conn.id.equals(id)) {
                return conn;
            }
        }
        return null;
    }

    public Conn findAutoConnect() {
        for (Conn conn : getConnections()) {
            if (conn.autoConnect) {
                return conn;
            }
        }
        return null;
    }

    // ------------------------------------------------------------ settings

    /**
     * Stable per-install id. Macro Deck uses it to remember which clients the
     * host has already accepted, so it must survive restarts.
     */
    public String getClientId() {
        String clientId = prefs.getString(KEY_CLIENT_ID, null);
        if (clientId == null) {
            clientId = UUID.randomUUID().toString();
            prefs.edit().putString(KEY_CLIENT_ID, clientId).apply();
        }
        return clientId;
    }

    public int getLongPressDelay() {
        return prefs.getInt(KEY_LONG_PRESS_DELAY, 1000);
    }

    public void setLongPressDelay(int delay) {
        prefs.edit().putInt(KEY_LONG_PRESS_DELAY, delay).apply();
    }

    public boolean getSkipSslValidation() {
        return prefs.getBoolean(KEY_SKIP_SSL, false);
    }

    public void setSkipSslValidation(boolean skip) {
        prefs.edit().putBoolean(KEY_SKIP_SSL, skip).apply();
    }

    public boolean getKeepAwake() {
        return prefs.getBoolean(KEY_KEEP_AWAKE, true);
    }

    public void setKeepAwake(boolean keepAwake) {
        prefs.edit().putBoolean(KEY_KEEP_AWAKE, keepAwake).apply();
    }
}
