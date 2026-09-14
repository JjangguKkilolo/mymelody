using System.Windows;
using System.Windows.Media.Imaging;

namespace MyMelody.App;

// Every expression keeps the same canvas, so blinking never changes the layout or hit area.
internal static class SpriteSheet
{
    private static readonly Dictionary<string, BitmapSource> Cache = new();
    internal const int Columns = 2;
    internal const int Stages = 3;

    internal static string PathFor(string id) => Path.Combine(AppContext.BaseDirectory, "Assets", "Characters", $"{id}.png");

    internal static BitmapSource? Frame(string id, int stage, bool blink)
    {
        if (stage is < 1 or > Stages) throw new ArgumentOutOfRangeException(nameof(stage));
        var path = PathFor(id);
        if (File.Exists(path)) return GridFrame(path, stage, blink);

        // Old development output can still be opened while the new artwork is being prepared.
        var legacy = Path.Combine(AppContext.BaseDirectory, "Assets", "Characters", $"{id}-{stage}.png");
        var separateBlink = Path.Combine(AppContext.BaseDirectory, "Assets", "Characters", $"{id}-{stage}-blink.png");
        if (blink && File.Exists(separateBlink)) legacy = separateBlink;
        var bitmap = Load(legacy);
        if (bitmap == null || bitmap.PixelWidth != bitmap.PixelHeight * 2) return bitmap;
        return Crop(legacy, bitmap, blink ? 1 : 0, 0, bitmap.PixelHeight);
    }

    internal static BitmapSource GridFrame(string path, int stage, bool blink)
    {
        if (stage is < 1 or > Stages) throw new ArgumentOutOfRangeException(nameof(stage));
        var bitmap = Load(path) ?? throw new InvalidDataException("Missing character sprite sheet: " + path);
        if (bitmap.PixelWidth % Columns != 0 || bitmap.PixelHeight % Stages != 0 ||
            bitmap.PixelWidth / Columns != bitmap.PixelHeight / Stages)
            throw new InvalidDataException("Character sprite sheet must contain two columns and three rows of equal square cells: " + path);
        return Crop(path, bitmap, blink ? 1 : 0, stage - 1, bitmap.PixelWidth / Columns);
    }

    internal static BitmapSource? Load(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path)) return null;
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze();
        Cache[path] = bitmap;
        return bitmap;
    }

    private static BitmapSource Crop(string path, BitmapSource bitmap, int column, int row, int size)
    {
        var key = $"{path}#{column}:{row}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var cell = new CroppedBitmap(bitmap, new Int32Rect(column * size, row * size, size, size));
        cell.Freeze(); Cache[key] = cell;
        return cell;
    }
}
