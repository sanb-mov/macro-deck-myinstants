import {NgModule} from "@angular/core";
import {ButtonWidgetComponent} from "./button-widget/button-widget.component";
import {NgIf, NgStyle} from "@angular/common";
import {TouchEventModule} from "ng2-events";
import {EmptyWidgetComponent} from "./empty-widget/empty-widget.component";
import {SliderWidgetComponent} from "./slider-widget/slider-widget.component";
import {SliderGroupWidgetComponent} from "./slider-group-widget/slider-group-widget.component";

@NgModule({
    imports: [
        NgStyle,
        TouchEventModule,
        NgIf,
        ButtonWidgetComponent,
        EmptyWidgetComponent,
        SliderWidgetComponent,
        SliderGroupWidgetComponent
    ],
    exports: [
        TouchEventModule
    ]
})
export class WidgetContentComponentsModule {
}
