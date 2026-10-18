import {Component, ElementRef, OnDestroy, OnInit, ViewChild} from '@angular/core';
import {NgStyle} from "@angular/common";
import {Subscription} from "rxjs";
import {Widget} from "../../datatypes/widgets/widget";
import {SliderWidget} from "../../datatypes/widgets/slider-widget";
import {WidgetGridComponent} from "../../pages/deck/widget-grid/widget-grid.component";
import {MacroDeckService} from "../../services/macro-deck/macro-deck.service";
import {WidgetInteractionType} from "../../enums/widget-interaction-type";
import {SliderStyle} from "../../enums/slider-style";

/**
 * Vertical volume slider. Dragging anywhere on the track sets the value, and the
 * button at the bottom toggles mute when the provider supports it.
 */
@Component({
  selector: 'app-slider-widget',
  templateUrl: './slider-widget.component.html',
  styleUrls: ['./slider-widget.component.scss'],
  imports: [
    NgStyle
  ]
})
export class SliderWidgetComponent implements OnInit, OnDestroy {
  @ViewChild('track', {static: false}) trackElement!: ElementRef<HTMLElement>;

  widget: Widget | undefined;
  content: SliderWidget | undefined;

  /** Value shown right now. While dragging this is the local value, not the server's. */
  displayValue: number = 0;

  borderRadius: string = '0pt';

  /** Drives which of the three looks the template and stylesheet render. */
  styleClass: string = 'classic';

  private dragging: boolean = false;

  /** Server updates arriving mid-drag are ignored so the thumb does not jump back */
  private lastSentValue: number | undefined;

  private subscription: Subscription = new Subscription();

  constructor(private macroDeckService: MacroDeckService) {
  }

  ngOnInit(): void {
    this.subscription.add(WidgetGridComponent.updated.subscribe(() => {
      this.borderRadius = WidgetGridComponent.borderRadiusPoints + 'pt';
    }));
  }

  ngOnDestroy(): void {
    this.subscription.unsubscribe();
  }

  updateWidget(widget: Widget) {
    this.widget = widget;
    this.content = widget.widgetContent as SliderWidget;
    this.borderRadius = WidgetGridComponent.borderRadiusPoints + 'pt';

    if (!this.dragging && this.content) {
      this.displayValue = this.clamp(this.content.value);
    }

    this.styleClass = SliderWidgetComponent.styleClassOf(this.content?.style);
  }

  private static styleClassOf(style: SliderStyle | undefined): string {
    switch (style) {
      case SliderStyle.Capsule:
        return 'capsule';
      case SliderStyle.Knob:
        return 'knob-style';
      default:
        return 'classic';
    }
  }

  get showThumb(): boolean {
    return this.styleClass === 'classic';
  }

  get showKnob(): boolean {
    return this.styleClass === 'knob-style';
  }

  get knobStyle() {
    // The knob is a circle riding on the fill level; sizing it from the track width
    // keeps it proportional on any deck size.
    const size = 'clamp(18px, 170%, 46px)';
    return {
      'width': size,
      'height': size,
      'aspect-ratio': '1',
      'bottom': 'calc(' + (this.displayValue * 100) + '% - clamp(9px, 85%, 23px))',
      'transform': 'translateX(-50%)'
    };
  }

  get fillStyle() {
    return {
      'height': (this.displayValue * 100) + '%',
      'background-color': this.content?.muted ? '#5a5a5a' : (this.content?.colorHex ?? '#0067CD')
    };
  }

  get thumbStyle() {
    return {
      'bottom': 'calc(' + (this.displayValue * 100) + '% - 9px)'
    };
  }

  get percentText(): string {
    return Math.round(this.displayValue * 100) + '%';
  }

  onPointerDown(event: PointerEvent) {
    this.dragging = true;
    (event.target as HTMLElement).setPointerCapture?.(event.pointerId);
    this.applyPointer(event);
  }

  onPointerMove(event: PointerEvent) {
    if (!this.dragging) {
      return;
    }
    this.applyPointer(event);
  }

  onPointerUp(event: PointerEvent) {
    if (!this.dragging) {
      return;
    }

    this.dragging = false;
    (event.target as HTMLElement).releasePointerCapture?.(event.pointerId);
    this.emitValue(this.displayValue, true);
  }

  toggleMute(event: Event) {
    event.stopPropagation();

    if (this.widget === undefined || this.content === undefined || !this.content.supportsMute) {
      return;
    }

    const muted = !this.content.muted;
    this.content.muted = muted;

    this.macroDeckService.interaction.emit({
      widget: this.widget,
      widgetInteractionType: WidgetInteractionType.SliderMuteToggle,
      muted: muted
    });
  }

  private applyPointer(event: PointerEvent) {
    const track = this.trackElement?.nativeElement;
    if (!track) {
      return;
    }

    const rect = track.getBoundingClientRect();
    if (rect.height <= 0) {
      return;
    }

    const value = this.clamp((rect.bottom - event.clientY) / rect.height);
    this.displayValue = value;
    this.emitValue(value, false);
  }

  /**
   * Sends the value to Macro Deck. Intermediate values are only sent when they moved by
   * at least one percent so dragging does not flood the websocket.
   */
  private emitValue(value: number, force: boolean) {
    if (this.widget === undefined) {
      return;
    }

    const rounded = Math.round(value * 100) / 100;
    if (!force && this.lastSentValue !== undefined && Math.abs(rounded - this.lastSentValue) < 0.01) {
      return;
    }

    this.lastSentValue = rounded;

    if (this.content) {
      this.content.value = rounded;
    }

    this.macroDeckService.interaction.emit({
      widget: this.widget,
      widgetInteractionType: WidgetInteractionType.SliderChange,
      value: rounded
    });
  }

  private clamp(value: number): number {
    return Math.min(1, Math.max(0, value));
  }
}
