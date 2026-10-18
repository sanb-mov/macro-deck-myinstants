import {Injectable} from '@angular/core';
import {MacroDeckService} from "../macro-deck/macro-deck.service";
import {Protocol2Messages} from "../../datatypes/protocol2/protocol2-messages";
import {WebSocketSubject} from "rxjs/internal/observable/dom/WebSocketSubject";
import {Protocol2Button} from "../../datatypes/protocol2/protocol2-button";
import {Widget} from "../../datatypes/widgets/widget";
import {ButtonWidget} from "../../datatypes/widgets/button-widget";
import {Protocol2Slider, Protocol2SliderGroup} from "../../datatypes/protocol2/protocol2-slider";
import {SliderGroupWidget} from "../../datatypes/widgets/slider-group-widget";
import {SliderWidget} from "../../datatypes/widgets/slider-widget";
import {WidgetContentType} from "../../enums/widget-content-type";
import {WidgetInteraction} from "../../datatypes/widgets/widget-interaction";
import {WidgetInteractionType} from "../../enums/widget-interaction-type";
import {LoadingService} from "../loading/loading.service";
import {NavigationService} from "../navigation/navigation.service";
import {NavigationDestination} from "../../enums/navigation-destination";

@Injectable({
    providedIn: 'root'
})
export class Protocol2Service {

    private initialConfigReceived = false;

    private socket: WebSocketSubject<any> | undefined;

    constructor(private macroDeckService: MacroDeckService,
                private loadingService: LoadingService,
                private navigationService: NavigationService) {
        macroDeckService.interaction.subscribe(interaction => {
            this.handleInteraction(interaction);
        })

        macroDeckService.backRequested.subscribe(() => {
            this.send({"Method": "FOLDER_BACK"});
        })
    }

    async handleMessage(message: any) {
        if (!message.Method) {
            return;
        }

        switch (message.Method) {
            case "GET_CONFIG":
                this.macroDeckService.setConfig(message);
                if (!this.initialConfigReceived) {
                    this.initialConfigReceived = true;
                    await this.navigationService.navigateTo(NavigationDestination.Deck);
                    await this.loadingService.dismiss();
                }

                this.send(Protocol2Messages.getGetButtonsMessage());
                break;
            case "GET_BUTTONS":
                if (!this.initialConfigReceived) {
                    return;
                }

                let widgets: Widget[] = message.Buttons.map((button: Protocol2Button) => {
                    return this.mapProtocol2ButtonToWidget(button);
                });

                // Buttons carry their own navigation, so the overlay is only for sliders.
                this.macroDeckService.setCanGoBack(false);
                this.macroDeckService.setWidgets(widgets);
                break;
            case "UPDATE_BUTTON":
                if (!this.initialConfigReceived) {
                    return;
                }

                let widget = this.mapProtocol2ButtonToWidget(message.Buttons[0]);

                this.macroDeckService.updateWidget(widget);
                break;
            case "GET_SLIDERS":
                if (!this.initialConfigReceived) {
                    return;
                }

                let sliderWidgets: Widget[] = message.Sliders.map((slider: Protocol2Slider) => {
                    return this.mapProtocol2SliderToWidget(slider);
                });

                // The host reserves a cell per band for the group name so the client
                // can show which mix a row of sliders belongs to.
                const groups: Protocol2SliderGroup[] = message.Groups ?? [];
                for (const group of groups) {
                    sliderWidgets.push(this.mapProtocol2SliderGroupToWidget(group));
                }

                this.macroDeckService.setCanGoBack(message.CanGoBack === true);
                this.macroDeckService.setWidgets(sliderWidgets);
                break;
            case "UPDATE_SLIDER":
                if (!this.initialConfigReceived) {
                    return;
                }

                this.macroDeckService.updateWidget(this.mapProtocol2SliderToWidget(message.Sliders[0]));
                break;
            case "UPDATE_LABEL":
                if (!this.initialConfigReceived) {
                    return;
                }

                let receivedButton = message.Buttons[0] as Protocol2Button;
                let existingWidget = this.macroDeckService.widgets.find(x => x.row === receivedButton.Position_Y
                    && x.column === receivedButton.Position_X);
                if (existingWidget === undefined) {
                    return;
                }

                let existingWidgetContent = existingWidget?.widgetContent as ButtonWidget;
                existingWidgetContent.labelBase64 = receivedButton.LabelBase64;

                this.macroDeckService.updateWidget(existingWidget);
                break;
        }
    }

    setWebsocketSubject(socket: WebSocketSubject<any>) {
        this.initialConfigReceived = false;
        this.socket = socket;
    }

    private mapProtocol2ButtonToWidget(button: Protocol2Button): Widget {
        let buttonWidget: ButtonWidget = {
            iconBase64: button.IconBase64,
            labelBase64: button.LabelBase64,
        }
        return {
            backgroundColorHex: button.BackgroundColorHex,
            colSpan: 1,
            column: button.Position_X,
            row: button.Position_Y,
            rowSpan: 1,
            widgetContentType: WidgetContentType.button,
            widgetContent: buttonWidget
        }
    }

    private mapProtocol2SliderToWidget(slider: Protocol2Slider): Widget {
        let sliderWidget: SliderWidget = {
            sliderId: slider.SliderId,
            providerId: slider.ProviderId,
            displayName: slider.DisplayName,
            value: slider.Value,
            muted: slider.Muted,
            supportsMute: slider.SupportsMute,
            colorHex: slider.ColorHex,
            group: slider.Group,
            style: slider.Style ?? 0
        }
        return {
            backgroundColorHex: '#232323',
            colSpan: slider.ColSpan ?? 1,
            column: slider.Column,
            row: slider.Row,
            rowSpan: slider.RowSpan ?? 1,
            widgetContentType: WidgetContentType.slider,
            widgetContent: sliderWidget
        }
    }

    private mapProtocol2SliderGroupToWidget(group: Protocol2SliderGroup): Widget {
        let content: SliderGroupWidget = {
            name: group.Name
        }
        return {
            backgroundColorHex: '#1c1c1c',
            colSpan: 1,
            column: group.Column,
            row: group.Row,
            rowSpan: group.RowSpan ?? 1,
            widgetContentType: WidgetContentType.sliderGroup,
            widgetContent: content
        }
    }

    private send(payload: any) {
        this.socket?.next(payload);
    }

    private handleInteraction(interaction: WidgetInteraction) {
        // Slider interactions address the slider by id instead of by grid cell
        if (interaction.widgetInteractionType === WidgetInteractionType.SliderChange ||
            interaction.widgetInteractionType === WidgetInteractionType.SliderMuteToggle) {
            this.handleSliderInteraction(interaction);
            return;
        }

        let method: String | undefined;
        switch (interaction.widgetInteractionType) {
            case WidgetInteractionType.ButtonPress:
                method = "BUTTON_PRESS";
                break;
            case WidgetInteractionType.ButtonShortPressRelease:
                method = "BUTTON_RELEASE";
                break;
            case WidgetInteractionType.ButtonLongPress:
                method = "BUTTON_LONG_PRESS";
                break;
            case WidgetInteractionType.ButtonLongPressRelease:
                method = "BUTTON_LONG_PRESS_RELEASE";
                break;
        }
      this.send({
        "Method": method,
        "Message": `${interaction.widget.row}_${interaction.widget.column}`
      });
    }

    private handleSliderInteraction(interaction: WidgetInteraction) {
        const sliderWidget = interaction.widget.widgetContent as SliderWidget;
        if (!sliderWidget) {
            return;
        }

        if (interaction.widgetInteractionType === WidgetInteractionType.SliderChange) {
            this.send({
                "Method": "SLIDER_CHANGE",
                "SliderId": sliderWidget.sliderId,
                "ProviderId": sliderWidget.providerId,
                "Value": interaction.value ?? 0
            });
            return;
        }

        this.send({
            "Method": "SLIDER_MUTE",
            "SliderId": sliderWidget.sliderId,
            "ProviderId": sliderWidget.providerId,
            "Muted": interaction.muted ?? false
        });
    }
}
