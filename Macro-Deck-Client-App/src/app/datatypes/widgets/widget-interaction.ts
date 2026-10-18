import {WidgetInteractionType} from "../../enums/widget-interaction-type";
import {Widget} from "./widget";

export interface WidgetInteraction {
    widget: Widget,
    widgetInteractionType: WidgetInteractionType,

    /** New value (0.0 - 1.0) for SliderChange interactions */
    value?: number,

    /** New mute state for SliderMuteToggle interactions */
    muted?: boolean
}
