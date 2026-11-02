using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorPlusApp;

// Файл курсора .cur: несколько размеров, внутри каждого — PNG (Windows читает такие, как и значки с PNG).
static class CurFile
{
    static byte[] Png(CurImg i)
    {
        var bmp = BitmapSource.Create(i.W, i.H, 96, 96, PixelFormats.Bgra32, null, i.Bgra, i.W * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static void Write(string path, IReadOnlyList<CurImg> imgs)
    {
        var pngs = imgs.Select(Png).ToList();
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0); bw.Write((ushort)2); bw.Write((ushort)imgs.Count);   // 2 — курсор
        var offset = 6 + 16 * imgs.Count;
        for (var i = 0; i < imgs.Count; i++)
        {
            var im = imgs[i];
            bw.Write((byte)(im.W >= 256 ? 0 : im.W)); bw.Write((byte)(im.H >= 256 ? 0 : im.H));
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((ushort)Math.Clamp(im.HotX, 0, im.W - 1)); bw.Write((ushort)Math.Clamp(im.HotY, 0, im.H - 1));
            bw.Write((uint)pngs[i].Length); bw.Write((uint)offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) bw.Write(p);
    }
}
