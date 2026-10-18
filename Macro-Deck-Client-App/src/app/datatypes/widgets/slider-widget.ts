import {WidgetContent} from "./widget-content";
import {SliderStyle} from "../../enums/slider-style";

export interface SliderWidget extends WidgetContent {
    sliderId: string,
    providerId: string,
    displayName: string,
    value: number,
    muted: boolean,
    supportsMute: boolean,
    colorHex: string,
    group: string | undefined,
    style: SliderStyle
}
