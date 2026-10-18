using System.IO;
using Newtonsoft.Json;
using Serilog;
using SuchByte.MacroDeck.StartupConfig;

namespace SuchByte.MacroDeck.Soundboard;

/// <summary>
/// Size of one soundboard page. Rows times columns is the number of sounds per page, so changing it
/// re-flows the sounds over the pages. Shared by the main window and the clients.
/// </summary>
public static class SoundboardSettings
{
    public const int MaxRows = 8;
    public const int MaxColumns = 12;

    private const int DefaultRows = 4;
    private const int DefaultColumns = 6;

    private static readonly ILogger Logger = Log.ForContext(typeof(SoundboardSettings));
    private static readonly Lock SaveLock = new();

    private sealed class Data
    {
        public int Rows { get; set; } = DefaultRows;
        public int Columns { get; set; } = DefaultColumns;
    }

    private static Data _data = Load();

    public static int Rows => _data.Rows;
    public static int Columns => _data.Columns;
    public static int PageSize => _data.Rows * _data.Columns;

    private static string FilePath => Path.Combine(ApplicationPaths.UserDirectoryPath, "soundboard.json");

    public static void Set(int rows, int columns)
    {
        _data = new Data
        {
            Rows = Math.Clamp(rows, 1, MaxRows),
            Columns = Math.Clamp(columns, 1, MaxColumns)
        };

        lock (SaveLock)
        {
            try
            {
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(_data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Could not save the soundboard settings");
            }
        }
    }

    private static Data Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonConvert.DeserializeObject<Data>(File.ReadAllText(FilePath)) ?? new Data();
                data.Rows = Math.Clamp(data.Rows, 1, MaxRows);
                data.Columns = Math.Clamp(data.Columns, 1, MaxColumns);
                return data;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Could not read the soundboard settings");
        }

        return new Data();
    }
}
