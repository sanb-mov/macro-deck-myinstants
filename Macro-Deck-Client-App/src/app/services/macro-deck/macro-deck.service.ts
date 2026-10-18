import {EventEmitter, Injectable, Output} from '@angular/core';
import {WebsocketService} from "../websocket/websocket.service";
import {Widget} from "../../datatypes/widgets/widget";
import {WidgetInteraction} from "../../datatypes/widgets/widget-interaction";

@Injectable({
  providedIn: 'root'
})
export class MacroDeckService {
  @Output() configUpdate = new EventEmitter();
  @Output() interaction = new EventEmitter<WidgetInteraction>();

  /** Raised when the user asks to leave the current folder. */
  @Output() backRequested = new EventEmitter<void>();

  /** true while the current folder is a slider folder that has a parent. */
  canGoBack: boolean = false;

  widgets: Widget[] = [];
  rows: number = 3;
  columns: number = 5;
  buttonSpacing: number = 10;
  buttonRadius: number = 40;
  buttonBackground: boolean = true;

  constructor() {
  }

  setConfig(message: any) {
    this.rows = message.Rows;
    this.columns = message.Columns;
    this.buttonSpacing = message.ButtonSpacing;
    this.buttonRadius = message.ButtonRadius;
    this.buttonBackground = message.ButtonBackground;
    this.configUpdate.emit();
  }

  setWidgets(widgets: Widget[]) {
    this.widgets = widgets;
  }

  setCanGoBack(canGoBack: boolean) {
    this.canGoBack = canGoBack;
  }

  goBack() {
    this.backRequested.emit();
  }

  updateWidget(widget: Widget) {
    let existingWidgetIndex = this.widgets.findIndex(x => x.row == widget.row && x.column == widget.column);
    if (existingWidgetIndex !== -1) {
      this.widgets[existingWidgetIndex] = widget;
    } else {
      this.widgets.push(widget);
    }
  }
}
