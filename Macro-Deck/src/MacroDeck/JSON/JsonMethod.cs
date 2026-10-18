namespace SuchByte.MacroDeck.JSON;

public enum JsonMethod
{
    CONNECTED,
    BUTTON_PRESS,
    BUTTON_RELEASE,
    BUTTON_LONG_PRESS,
    BUTTON_LONG_PRESS_RELEASE,
    GET_BUTTONS,
    GET_ICONS,
    UPDATE_BUTTON,
    UPDATE_LABEL,
    ICON_BASE64,
    GET_CONFIG,
    BUTTON_DONE,
    GET_INSTALLED_PLUGINS,
    GET_INSTALLED_ICON_PACKS,

    // Sliders
    GET_SLIDERS,
    UPDATE_SLIDER,
    SLIDER_CHANGE,
    SLIDER_MUTE,

    // Folder navigation from clients that have no button to do it (slider folders)
    FOLDER_BACK,

    // Soundboard folder, for clients that draw it themselves
    GET_SOUNDBOARD,
    SOUNDBOARD_ACTION
}
