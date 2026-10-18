package com.suchbyte.macrodeck.legacy;

import org.json.JSONObject;

/**
 * Label naming a band of sliders. The host reserves a grid cell for it so the deck
 * can show which mix a row belongs to (e.g. Sonar's Streaming and Monitoring).
 */
public class MdSliderGroup {

    public String name = "";
    public int row;
    public int column;
    public int rowSpan = 1;

    public static MdSliderGroup fromJson(JSONObject raw) {
        MdSliderGroup group = new MdSliderGroup();
        group.name = raw.isNull("Name") ? "" : raw.optString("Name", "");
        group.row = raw.optInt("Row", 0);
        group.column = raw.optInt("Column", 0);
        group.rowSpan = Math.max(raw.optInt("RowSpan", 1), 1);
        return group;
    }
}
