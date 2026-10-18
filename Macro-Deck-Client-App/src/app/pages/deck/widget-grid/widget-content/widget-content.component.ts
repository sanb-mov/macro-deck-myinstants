import {Component, ComponentRef, Input, OnDestroy, ViewChild, ViewContainerRef} from '@angular/core';
import {Widget} from "../../../../datatypes/widgets/widget";
import {WidgetContentType} from "../../../../enums/widget-content-type";
import {ButtonWidgetComponent} from "../../../../widget-content-components/button-widget/button-widget.component";
import {MacroDeckService} from "../../../../services/macro-deck/macro-deck.service";
import {WidgetInteractionType} from "../../../../enums/widget-interaction-type";
import {Subscription} from "rxjs";
import {EmptyWidgetComponent} from "../../../../widget-content-components/empty-widget/empty-widget.component";
import {SliderWidgetComponent} from "../../../../widget-content-components/slider-widget/slider-widget.component";
import {SliderGroupWidgetComponent} from "../../../../widget-content-components/slider-group-widget/slider-group-widget.component";

@Component({
  selector: 'app-widget-content',
  templateUrl: './widget-content.component.html',
  styleUrls: ['./widget-content.component.scss'],
})
export class WidgetContentComponent implements OnDestroy {
  @ViewChild("contentRef", { read: ViewContainerRef }) vcr!: ViewContainerRef;
  ref!: ComponentRef<any>

  @Input()
  set data(data: Widget | undefined) {
    this.updateContent(data);
  }

  currentContentType: WidgetContentType | undefined;
  private subscription: Subscription = new Subscription();
  private componentCreated: boolean = false;

  constructor() { }

  updateContent(data: Widget | undefined) {
    if (data?.widgetContentType !== this.currentContentType) {
      this.vcr?.clear();
      this.componentCreated = false;
    }

    if (this.vcr === undefined || data === undefined) {
      return;
    }

    this.currentContentType = data.widgetContentType;

    switch (data.widgetContentType) {
      case WidgetContentType.empty:
        if (!this.componentCreated) {
          this.ref = this.vcr.createComponent(EmptyWidgetComponent);
        }
        this.ref.instance.updateWidget(data);
        break;
      case WidgetContentType.button:
        if (!this.componentCreated) {
          this.ref = this.vcr.createComponent(ButtonWidgetComponent);
        }
        this.ref.instance.updateWidget(data);
        break;
      case WidgetContentType.slider:
        if (!this.componentCreated) {
          this.ref = this.vcr.createComponent(SliderWidgetComponent);
        }
        this.ref.instance.updateWidget(data);
        break;
      case WidgetContentType.sliderGroup:
        if (!this.componentCreated) {
          this.ref = this.vcr.createComponent(SliderGroupWidgetComponent);
        }
        this.ref.instance.updateWidget(data);
        break;
      default:
        break;
    }

    this.componentCreated = true;
    this.ref.location.nativeElement.setAttribute("class", "flex-grow-1")
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
  }
}
