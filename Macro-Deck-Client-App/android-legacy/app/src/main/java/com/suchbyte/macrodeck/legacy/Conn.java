package com.suchbyte.macrodeck.legacy;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.UUID;

/**
 * A saved Macro Deck host. Mirrors the Connection interface of the Ionic client
 * (src/app/datatypes/connection.ts), minus the USB-specific fields.
 */
public class Conn {

    public String id;
    public String name;
    public String host;
    public int port;
    public boolean ssl;
    public String token;
    public boolean autoConnect;

    public Conn() {
        this.id = UUID.randomUUID().toString();
        this.name = "";
        this.host = "";
        this.port = 8191;
        this.ssl = false;
        this.token = "";
        this.autoConnect = false;
    }

    public String url() {
        return (ssl ? "wss://" : "ws://") + host + ":" + port;
    }

    public String label() {
        String displayName = (name == null || name.trim().length() == 0) ? host : name;
        return displayName + "  —  " + host + ":" + port + (ssl ? " (SSL)" : "");
    }

    public JSONObject toJson() throws JSONException {
        JSONObject json = new JSONObject();
        json.put("id", id);
        json.put("name", name);
        json.put("host", host);
        json.put("port", port);
        json.put("ssl", ssl);
        json.put("token", token);
        json.put("autoConnect", autoConnect);
        return json;
    }

    public static Conn fromJson(JSONObject json) {
        Conn conn = new Conn();
        conn.id = json.optString("id", UUID.randomUUID().toString());
        conn.name = json.optString("name", "");
        conn.host = json.optString("host", "");
        conn.port = json.optInt("port", 8191);
        conn.ssl = json.optBoolean("ssl", false);
        conn.token = json.optString("token", "");
        conn.autoConnect = json.optBoolean("autoConnect", false);
        return conn;
    }
}
