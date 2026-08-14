using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CryptoSigTool;

internal static class PdfStampVectorRenderer
{
    private const double BezierCircle = 0.5522847498307936;

    public static byte[] Render(
        X509Certificate2 certificate,
        PdfStampSettings settings,
        double width,
        double height)
    {
        var style = PdfStampDesignCatalog.GetStyle(settings.Design);
        var builder = new StringBuilder(160_000);
        builder.AppendLine("q");
        builder.AppendLine("1 1 1 rg");
        builder.AppendLine($"0 0 {N(width)} {N(height)} re f");

        var inset = Math.Max(1.5, Math.Min(width, height) * 0.035);
        var inner = new RectangleF(
            (float)inset,
            (float)inset,
            (float)Math.Max(1, width - inset * 2),
            (float)Math.Max(1, height - inset * 2));
        var borderWidth = Math.Max(0.8, inner.Height * style.BorderWidthFactor);
        builder.AppendLine($"{StrokeColor(style.AccentColor)} {N(borderWidth)} w 1 J 1 j");
        if (style.RoundedBorder)
            AppendRoundedBorder(builder, inner, height, Math.Min(inner.Height * (style.AcrobatFieldLayout ? 0.105 : 0.13), 20));
        else
            builder.AppendLine($"{N(inner.Left)} {N(height - inner.Bottom)} {N(inner.Width)} {N(inner.Height)} re S");

        var logoWidth = AppendLogo(builder, settings.LogoPath, inner, height);
        var horizontalPadding = style.AcrobatFieldLayout ? inner.Width * 0.05F : (float)(inset * 1.55);
        var contentX = inner.X + logoWidth + horizontalPadding;
        var contentWidth = inner.Width - logoWidth - horizontalPadding * 2;
        var serial = string.IsNullOrWhiteSpace(certificate.SerialNumber) ? "не указан" : certificate.SerialNumber;
        var owner = certificate.GetNameInfo(X509NameType.SimpleName, false);
        if (string.IsNullOrWhiteSpace(owner)) owner = certificate.Subject;
        var fields = new List<PdfStampField>
        {
            new("Сертификат: ", serial),
            new("Владелец: ", owner),
            new("Действителен: ", $"с {certificate.NotBefore:dd.MM.yyyy} по {certificate.NotAfter:dd.MM.yyyy}")
        };
        if (settings.ShowSigningDate)
            fields.Add(new PdfStampField("Дата подписания: ", settings.SigningTime.ToString("dd.MM.yyyy HH:mm:ss")));

        var layout = PdfStampLayoutCalculator.Calculate(style, inner.Height, contentWidth, fields);
        using var fontFamily = new FontFamily("Arial");
        var headerStyle = style.BoldHeader ? FontStyle.Bold : FontStyle.Regular;
        var labelStyle = style.BoldLabels ? FontStyle.Bold : FontStyle.Regular;
        var y = inner.Y + layout.StartOffset;

        builder.AppendLine(FillColor(style.AccentColor));
        AppendText(builder, "ДОКУМЕНТ ПОДПИСАН", fontFamily, headerStyle, layout.HeaderSize,
            contentX, y, contentWidth, height, style.CenteredHeader);
        y += layout.HeaderHeight;
        AppendText(builder, "ЭЛЕКТРОННОЙ ПОДПИСЬЮ", fontFamily, headerStyle, layout.HeaderSize,
            contentX, y, contentWidth, height, style.CenteredHeader);
        y = inner.Y + layout.BodyStartOffset;

        builder.AppendLine("0 0 0 rg");
        if (style.AcrobatFieldLayout)
        {
            DrawAcrobatFields(builder, fontFamily, layout.BodySize, fields,
                contentX, ref y, contentWidth, layout.LineHeight, height);
        }
        else
        {
            foreach (var field in fields)
                DrawField(builder, fontFamily, labelStyle, layout.BodySize, field.Label, field.Value,
                    contentX, ref y, contentWidth, layout.LabelWidth, layout.LineHeight, height);
        }

        builder.AppendLine("Q");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static void DrawAcrobatFields(
        StringBuilder builder,
        FontFamily family,
        float fontSize,
        IReadOnlyList<PdfStampField> fields,
        float x,
        ref float y,
        float width,
        float lineHeight,
        double appearanceHeight)
    {
        AppendText(builder, fields[0].Label.TrimEnd(), family, FontStyle.Regular, fontSize,
            x, y, width, appearanceHeight, false);
        y += lineHeight;
        AppendText(builder, fields[0].Value, family, FontStyle.Regular, fontSize,
            x, y, width, appearanceHeight, false);
        y += lineHeight;
        foreach (var field in fields.Skip(1))
        {
            AppendText(builder, field.Label + field.Value, family, FontStyle.Regular, fontSize,
                x, y, width, appearanceHeight, false);
            y += lineHeight;
        }
    }

    private static void DrawField(
        StringBuilder builder,
        FontFamily family,
        FontStyle labelStyle,
        float fontSize,
        string label,
        string value,
        float x,
        ref float y,
        float width,
        float labelWidth,
        float lineHeight,
        double appearanceHeight)
    {
        AppendText(builder, label, family, labelStyle, fontSize, x, y, labelWidth, appearanceHeight, false);
        AppendText(builder, string.IsNullOrWhiteSpace(value) ? "не указан" : value,
            family, FontStyle.Regular, fontSize, x + labelWidth, y,
            Math.Max(1, width - labelWidth), appearanceHeight, false);
        y += lineHeight;
    }

    private static void AppendText(
        StringBuilder builder,
        string text,
        FontFamily family,
        FontStyle style,
        float emSize,
        float x,
        float y,
        float availableWidth,
        double appearanceHeight,
        bool centered)
    {
        var fitted = FitText(text, family, style, emSize, availableWidth);
        using var path = new GraphicsPath(FillMode.Winding);
        path.AddString(fitted, family, (int)style, emSize, PointF.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        var targetX = centered ? x + Math.Max(0, (availableWidth - bounds.Width) / 2) : x;
        var offsetX = targetX - bounds.X;
        var offsetY = y - bounds.Y;
        AppendPath(builder, path, offsetX, offsetY, appearanceHeight);
        builder.AppendLine("f");
    }

    private static void AppendPath(
        StringBuilder builder,
        GraphicsPath path,
        float offsetX,
        float offsetY,
        double appearanceHeight)
    {
        var points = path.PathPoints;
        var types = path.PathTypes;
        for (var index = 0; index < points.Length; index++)
        {
            var kind = (PathPointType)(types[index] & (byte)PathPointType.PathTypeMask);
            if (kind == PathPointType.Start)
            {
                AppendPoint(builder, points[index], offsetX, offsetY, appearanceHeight, "m");
            }
            else if (kind == PathPointType.Line)
            {
                AppendPoint(builder, points[index], offsetX, offsetY, appearanceHeight, "l");
            }
            else if (kind == PathPointType.Bezier3 && index + 2 < points.Length)
            {
                AppendCoordinates(builder, points[index], offsetX, offsetY, appearanceHeight);
                builder.Append(' ');
                AppendCoordinates(builder, points[index + 1], offsetX, offsetY, appearanceHeight);
                builder.Append(' ');
                AppendCoordinates(builder, points[index + 2], offsetX, offsetY, appearanceHeight);
                builder.AppendLine(" c");
                index += 2;
            }

            if ((types[index] & (byte)PathPointType.CloseSubpath) != 0)
                builder.AppendLine("h");
        }
    }

    private static void AppendPoint(
        StringBuilder builder,
        PointF point,
        float offsetX,
        float offsetY,
        double appearanceHeight,
        string operation)
    {
        AppendCoordinates(builder, point, offsetX, offsetY, appearanceHeight);
        builder.Append(' ').AppendLine(operation);
    }

    private static void AppendCoordinates(
        StringBuilder builder,
        PointF point,
        float offsetX,
        float offsetY,
        double appearanceHeight)
    {
        builder.Append(N(point.X + offsetX));
        builder.Append(' ');
        builder.Append(N(appearanceHeight - (point.Y + offsetY)));
    }

    private static string FitText(string value, FontFamily family, FontStyle style, float emSize, float width)
    {
        if (PdfStampLayoutCalculator.MeasureText(value, family, style, emSize) <= width) return value;
        const string suffix = "…";
        var low = 0;
        var high = value.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (PdfStampLayoutCalculator.MeasureText(value[..middle] + suffix, family, style, emSize) <= width) low = middle;
            else high = middle - 1;
        }
        return value[..low] + suffix;
    }

    private static float AppendLogo(StringBuilder builder, string? path, RectangleF inner, double appearanceHeight)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;
        try
        {
            using var source = Image.FromFile(path);
            var maxWidth = inner.Width * 0.24F;
            var maxHeight = inner.Height * 0.76F;
            var scale = Math.Min(maxWidth / source.Width, maxHeight / source.Height);
            var drawWidth = source.Width * scale;
            var drawHeight = source.Height * scale;
            var pixelScale = Math.Min(1F, 320F / Math.Max(source.Width, source.Height));
            var pixelWidth = Math.Max(1, (int)Math.Round(source.Width * pixelScale));
            var pixelHeight = Math.Max(1, (int)Math.Round(source.Height * pixelScale));
            using var bitmap = new Bitmap(pixelWidth, pixelHeight, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, pixelWidth, pixelHeight);
            }

            using var output = new MemoryStream();
            var jpeg = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 96L);
            bitmap.Save(output, jpeg, parameters);
            var x = inner.X + (maxWidth - drawWidth) / 2 + 2;
            var y = inner.Y + (inner.Height - drawHeight) / 2;
            builder.AppendLine("q");
            builder.AppendLine($"{N(drawWidth)} 0 0 {N(drawHeight)} {N(x)} {N(appearanceHeight - y - drawHeight)} cm");
            builder.AppendLine($"BI /W {pixelWidth} /H {pixelHeight} /CS /RGB /BPC 8 /F [/AHx /DCT] ID");
            builder.Append(Convert.ToHexString(output.ToArray()));
            builder.AppendLine(">");
            builder.AppendLine("EI");
            builder.AppendLine("Q");
            return maxWidth;
        }
        catch
        {
            return 0;
        }
    }

    private static void AppendRoundedBorder(StringBuilder builder, RectangleF rectangle, double appearanceHeight, double radius)
    {
        var left = rectangle.Left;
        var right = rectangle.Right;
        var bottom = appearanceHeight - rectangle.Bottom;
        var top = appearanceHeight - rectangle.Top;
        var control = radius * BezierCircle;
        builder.AppendLine($"{N(left + radius)} {N(bottom)} m");
        builder.AppendLine($"{N(right - radius)} {N(bottom)} l");
        builder.AppendLine($"{N(right - radius + control)} {N(bottom)} {N(right)} {N(bottom + radius - control)} {N(right)} {N(bottom + radius)} c");
        builder.AppendLine($"{N(right)} {N(top - radius)} l");
        builder.AppendLine($"{N(right)} {N(top - radius + control)} {N(right - radius + control)} {N(top)} {N(right - radius)} {N(top)} c");
        builder.AppendLine($"{N(left + radius)} {N(top)} l");
        builder.AppendLine($"{N(left + radius - control)} {N(top)} {N(left)} {N(top - radius + control)} {N(left)} {N(top - radius)} c");
        builder.AppendLine($"{N(left)} {N(bottom + radius)} l");
        builder.AppendLine($"{N(left)} {N(bottom + radius - control)} {N(left + radius - control)} {N(bottom)} {N(left + radius)} {N(bottom)} c h S");
    }

    private static string FillColor(Color color) =>
        $"{N(color.R / 255D)} {N(color.G / 255D)} {N(color.B / 255D)} rg";

    private static string StrokeColor(Color color) =>
        $"{N(color.R / 255D)} {N(color.G / 255D)} {N(color.B / 255D)} RG";

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
