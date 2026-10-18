import {WidgetContent} from "./widget-content";

/** Label naming the band of sliders next to it (e.g. "Streaming" / "Monitoring"). */
export interface SliderGroupWidget extends WidgetContent {
    name: string
}
