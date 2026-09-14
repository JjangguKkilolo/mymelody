using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyMelody.Core;

namespace MyMelody.App;

// Source cells remain untouched. Layout follows the visible silhouette instead of PNG padding.
internal static class SpritePresentation
{
    private const double CanvasSize = 512;
    private const double ContentLimit = 480;
    private const double Baseline = 496;
    private const byte VisibleAlpha = 16;
    private const int EdgePadding = 2;
    private static readonly Lazy<Dictionary<(string Id, int Stage, bool Blink), ImageSource>> Frames = new(BuildFrames);

    internal static ImageSource Frame(string id, int stage, bool blink) => Frames.Value[(id, stage, blink)];

    private sealed record Expression(BitmapSource Bitmap, Int32Rect Bounds);
    private sealed record Pair(string Id, int Stage, Expression Open, Expression Blink)
    {
        // A shared scale prevents blinking from making a character grow or shrink.
        public int Height => Math.Max(Open.Bounds.Height, Blink.Bounds.Height);
        public int Width => Math.Max(Open.Bounds.Width, Blink.Bounds.Width);
    }

    private static Dictionary<(string, int, bool), ImageSource> BuildFrames()
    {
        var pairs = new List<Pair>();
        foreach (var character in CharacterCatalog.All)
        {
            var path = SpriteSheet.PathFor(character.Id);
            if (!File.Exists(path)) continue;
            for (var stage = 1; stage <= SpriteSheet.Stages; stage++)
            {
                var open = SpriteSheet.GridFrame(path, stage, false);
                var blink = SpriteSheet.GridFrame(path, stage, true);
                pairs.Add(new Pair(character.Id, stage, new(open, VisibleBounds(open)), new(blink, VisibleBounds(blink))));
            }
        }

        // Every appearance has the same height. The widest costume sets the available width.
        double widestAspect = pairs.Count == 0 ? 1 : pairs.Max(pair => pair.Width / (double)pair.Height);
        double height = ContentLimit / Math.Max(1, widestAspect);
        var result = new Dictionary<(string, int, bool), ImageSource>();
        foreach (var pair in pairs)
        {
            double scale = height / pair.Height;
            result[(pair.Id, pair.Stage, false)] = Draw(pair.Open, scale);
            result[(pair.Id, pair.Stage, true)] = Draw(pair.Blink, scale);
        }
        return result;
    }

    private static Int32Rect VisibleBounds(BitmapSource bitmap)
    {
        var rgba = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[rgba.PixelWidth * rgba.PixelHeight * 4];
        rgba.CopyPixels(pixels, rgba.PixelWidth * 4, 0);
        int left = rgba.PixelWidth, top = rgba.PixelHeight, right = -1, bottom = -1;
        for (int y = 0, offset = 3; y < rgba.PixelHeight; y++)
        for (int x = 0; x < rgba.PixelWidth; x++, offset += 4)
        {
            // Ignore transparent RGB and barely visible alpha residue around generated sprites.
            if (pixels[offset] < VisibleAlpha) continue;
            left = Math.Min(left, x); right = Math.Max(right, x);
            top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        if (right < left) throw new InvalidDataException("Character expression has no visible pixels.");
        return new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    private static ImageSource Draw(Expression expression, double scale)
    {
        var bounds = expression.Bounds;
        int left = Math.Max(0, bounds.X - EdgePadding), top = Math.Max(0, bounds.Y - EdgePadding);
        int right = Math.Min(expression.Bitmap.PixelWidth, bounds.X + bounds.Width + EdgePadding);
        int bottom = Math.Min(expression.Bitmap.PixelHeight, bounds.Y + bounds.Height + EdgePadding);
        var crop = new CroppedBitmap(expression.Bitmap, new Int32Rect(left, top, right - left, bottom - top));
        crop.Freeze();

        // Register each source expression by its center and feet. This removes source-sheet
        // offsets while both expressions retain the same scale and full aspect ratio.
        var destination = new Rect(
            CanvasSize / 2 + (left - bounds.X - bounds.Width / 2.0) * scale,
            Baseline + (top - bounds.Y - bounds.Height) * scale,
            crop.PixelWidth * scale, crop.PixelHeight * scale);
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, CanvasSize, CanvasSize))));
        drawing.Children.Add(new ImageDrawing(crop, destination));
        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
