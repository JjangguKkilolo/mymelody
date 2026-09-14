using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MyMelody.App;

internal static class AppIcon
{
    internal static readonly Uri ResourceUri = new("pack://application:,,,/MyMelodyPractice;component/Assets/Brand/app.ico");

    internal static ImageSource LoadWindowIcon()
    {
        var frame = BitmapFrame.Create(ResourceUri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    internal static Drawing.Icon CreateTrayIcon()
    {
        using var stream = Application.GetResourceStream(ResourceUri)?.Stream
            ?? throw new InvalidDataException("The bundled application icon is missing.");
        using var source = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        // The cloned native icon owns its handle independently of the resource stream.
        return (Drawing.Icon)source.Clone();
    }
}
