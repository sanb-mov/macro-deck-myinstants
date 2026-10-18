export interface Protocol2Slider {
    SliderId: string,
    ProviderId: string,
    DisplayName: string,
    Position: number,
    Value: number,
    Muted: boolean,
    SupportsMute: boolean,
    ColorHex: string,
    IconBase64: string | undefined,
    Group: string | undefined,
    Row: number,
    Column: number,
    RowSpan: number,
    ColSpan: number,
    Style: number
}

export interface Protocol2SliderGroup {
    Name: string,
    Row: number,
    Column: number,
    RowSpan: number
}
