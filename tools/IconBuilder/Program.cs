using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: IconBuilder <source.png> <output.ico>");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
using var source = new Bitmap(sourcePath);
var bounds = FindVisibleBounds(source);
var cropPadding = Math.Max(2, (int)Math.Ceiling(Math.Max(bounds.Width, bounds.Height) * 0.015));
bounds.Inflate(cropPadding, cropPadding);
bounds.Intersect(new Rectangle(0, 0, source.Width, source.Height));

var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
var images = sizes.Select(size => RenderPng(source, bounds, size)).ToArray();
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

using (var stream = File.Create(outputPath))
using (var writer = new BinaryWriter(stream))
{
    writer.Write((ushort)0); // reserved
    writer.Write((ushort)1); // icon
    writer.Write((ushort)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var index = 0; index < sizes.Length; index++)
    {
        var size = sizes[index];
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(images[index].Length);
        writer.Write(offset);
        offset += images[index].Length;
    }
    foreach (var image in images) writer.Write(image);
}

Console.WriteLine($"Created {outputPath}");
Console.WriteLine($"Source: {source.Width}x{source.Height}; visible bounds: {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}");
Console.WriteLine("Sizes: " + string.Join(", ", sizes.Select(x => $"{x}x{x}")));
return 0;

static Rectangle FindVisibleBounds(Bitmap source)
{
    var left = source.Width;
    var top = source.Height;
    var right = -1;
    var bottom = -1;
    for (var y = 0; y < source.Height; y++)
    for (var x = 0; x < source.Width; x++)
    {
        if (source.GetPixel(x, y).A <= 2) continue;
        left = Math.Min(left, x);
        top = Math.Min(top, y);
        right = Math.Max(right, x);
        bottom = Math.Max(bottom, y);
    }
    if (right < left || bottom < top) throw new InvalidDataException("The source image is fully transparent.");
    return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
}

static byte[] RenderPng(Bitmap source, Rectangle sourceBounds, int size)
{
    using var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(result);
    graphics.Clear(Color.Transparent);
    graphics.CompositingMode = CompositingMode.SourceCopy;
    graphics.CompositingQuality = CompositingQuality.HighQuality;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    graphics.SmoothingMode = SmoothingMode.HighQuality;

    var inset = Math.Max(1, (int)Math.Round(size * 0.035));
    var available = size - inset * 2;
    var scale = Math.Min((double)available / sourceBounds.Width, (double)available / sourceBounds.Height);
    var width = Math.Max(1, (int)Math.Round(sourceBounds.Width * scale));
    var height = Math.Max(1, (int)Math.Round(sourceBounds.Height * scale));
    var destination = new Rectangle((size - width) / 2, (size - height) / 2, width, height);
    graphics.DrawImage(source, destination, sourceBounds, GraphicsUnit.Pixel);

    using var memory = new MemoryStream();
    result.Save(memory, ImageFormat.Png);
    return memory.ToArray();
}
