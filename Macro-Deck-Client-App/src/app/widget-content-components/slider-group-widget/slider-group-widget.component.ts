import {Component} from '@angular/core';
import {Widget} from "../../datatypes/widgets/widget";
import {SliderGroupWidget} from "../../datatypes/widgets/slider-group-widget";

/**
 * Label that names the band of sliders beside it. The text is rotated so a long
 * name still fits the narrow column the host reserves for it.
 */
@Component({
  selector: 'app-slider-group-widget',
  templateUrl: './slider-group-widget.component.html',
  styleUrls: ['./slider-group-widget.component.scss']
})
export class SliderGroupWidgetComponent {
  name: string = '';

  updateWidget(widget: Widget) {
    this.name = (widget.widgetContent as SliderGroupWidget)?.name ?? '';
  }
}
