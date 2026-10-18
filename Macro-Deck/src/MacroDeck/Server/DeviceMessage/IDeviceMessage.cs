using SuchByte.MacroDeck.Sliders;

namespace SuchByte.MacroDeck.Server.DeviceMessage;

public interface IDeviceMessage
{
    public void Connected(MacroDeckClient macroDeckClient);
    public void SendConfiguration(MacroDeckClient macroDeckClient);
    public void SendAllButtons(MacroDeckClient macroDeckClient);
    public void UpdateButton(MacroDeckClient macroDeckClient, ActionButton.ActionButton actionButton);
    public void SendAllSliders(MacroDeckClient macroDeckClient, IReadOnlyList<MacroDeckSlider> sliders);
    public void UpdateSlider(MacroDeckClient macroDeckClient, MacroDeckSlider slider);
}
