using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace SuchByte.MacroDeck.Folders;

public class MacroDeckFolder : IDisposable
{
    private IntPtr _bufferPtr;
    public int BUFFER_SIZE = 1024 * 1024; // 1 MB
    private bool _disposed;

    public MacroDeckFolder()
    {
        _bufferPtr = Marshal.AllocHGlobal(BUFFER_SIZE);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Free any other managed objects here.
        }

        foreach (var actionButton in ActionButtons)
        {
            actionButton.Dispose();
        }

        // Free any unmanaged objects here.
        Marshal.FreeHGlobal(_bufferPtr);
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MacroDeckFolder()
    {
        Dispose(false);
    }

    [JsonIgnore] public bool IsRootFolder => DisplayName.Equals("*Root*");

    public string FolderId { get; set; }
    public string DisplayName { get; set; }
    public List<string> Childs { get; set; } = new();

    public List<ActionButton.ActionButton> ActionButtons { get; set; }
    /// <summary>
    /// true = this folder does not contain buttons but the sliders of <see cref="SliderProviderId"/>
    /// </summary>
    public bool IsSliderFolder { get; set; }

    /// <summary>
    /// Id of the ISliderProvider whose sliders are shown when <see cref="IsSliderFolder"/> is true
    /// </summary>
    public string SliderProviderId { get; set; } = "";

    /// <summary>
    /// Visual style used to render this folder's sliders on every client
    /// </summary>
    public Sliders.SliderStyle SliderStyle { get; set; } = Sliders.SliderStyle.Classic;

    /// <summary>
    /// true = this folder does not contain buttons but the trending sounds of myinstants.com,
    /// which the clients get as ordinary buttons (see SoundboardFolderService)
    /// </summary>
    public bool IsSoundboardFolder { get; set; }

    public List<string> ApplicationsFocusDevices { get; set; } = new();
    public string ApplicationToTrigger { get; set; } = "";
}
