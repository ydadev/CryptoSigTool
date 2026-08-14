using System.Security.Cryptography.X509Certificates;
using PdfSharp.Drawing;
using PdfSharp.Pdf.Annotations;

namespace CryptoSigTool;

internal sealed class Fz63SignatureAppearance : IAnnotationAppearanceHandler
{
    private readonly X509Certificate2 _certificate;
    private readonly PdfStampSettings _settings;

    public Fz63SignatureAppearance(X509Certificate2 certificate, PdfStampSettings settings)
    {
        _certificate = certificate;
        _settings = settings;
    }

    public void DrawAppearance(XGraphics graphics, XRect rect)
    {
        var style = PdfStampDesignCatalog.GetStyle(_settings.Design);
        var accentColor = XColor.FromArgb(style.AccentColor.A, style.AccentColor.R, style.AccentColor.G, style.AccentColor.B);
        var accentBrush = new XSolidBrush(accentColor);
        var inset = Math.Max(1.5, Math.Min(rect.Width, rect.Height) * 0.035);
        var inner = new XRect(inset, inset, rect.Width - inset * 2, rect.Height - inset * 2);
        var borderWidth = Math.Max(0.8, inner.Height * style.BorderWidthFactor);
        var borderPen = new XPen(accentColor, borderWidth);
        if (style.RoundedBorder)
        {
            var radius = Math.Min(inner.Height * (style.AcrobatFieldLayout ? 0.105 : 0.13), 20);
            graphics.DrawRoundedRectangle(borderPen, XBrushes.White, inner, new XSize(radius * 2, radius * 2));
        }
        else
        {
            graphics.DrawRectangle(borderPen, XBrushes.White, inner);
        }

        var logoWidth = DrawLogo(graphics, inner);
        var horizontalPadding = style.AcrobatFieldLayout ? inner.Width * 0.05 : inset * 1.55;
        var contentX = inner.X + logoWidth + horizontalPadding;
        var contentWidth = inner.Width - logoWidth - horizontalPadding * 2;
        var serial = string.IsNullOrWhiteSpace(_certificate.SerialNumber) ? "не указан" : _certificate.SerialNumber;
        var owner = _certificate.GetNameInfo(X509NameType.SimpleName, false);
        if (string.IsNullOrWhiteSpace(owner)) owner = _certificate.Subject;
        var validity = $"с {_certificate.NotBefore:dd.MM.yyyy} по {_certificate.NotAfter:dd.MM.yyyy}";
        var fields = new List<PdfStampField>
        {
            new("Сертификат: ", serial),
            new("Владелец: ", owner),
            new("Действителен: ", validity)
        };
        if (_settings.ShowSigningDate)
            fields.Add(new PdfStampField("Дата подписания: ", _settings.SigningTime.ToString("dd.MM.yyyy HH:mm:ss")));

        var layout = PdfStampLayoutCalculator.Calculate(style, (float)inner.Height, (float)contentWidth, fields);
        var headerFont = new XFont("Arial", layout.HeaderSize, style.BoldHeader ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        var bodyFont = new XFont("Arial", layout.BodySize, XFontStyleEx.Regular);
        var labelFont = new XFont("Arial", layout.BodySize, style.BoldLabels ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        var y = inner.Y + layout.StartOffset;
        var headerFormat = style.CenteredHeader ? XStringFormats.TopCenter : XStringFormats.TopLeft;
        graphics.DrawString("ДОКУМЕНТ ПОДПИСАН", headerFont, accentBrush,
            new XRect(contentX, y, contentWidth, layout.HeaderHeight), headerFormat);
        y += layout.HeaderHeight;
        graphics.DrawString("ЭЛЕКТРОННОЙ ПОДПИСЬЮ", headerFont, accentBrush,
            new XRect(contentX, y, contentWidth, layout.HeaderHeight), headerFormat);
        y = inner.Y + layout.BodyStartOffset;

        if (style.AcrobatFieldLayout)
        {
            DrawAcrobatFields(graphics, bodyFont, fields, contentX, ref y, contentWidth, layout.LineHeight);
        }
        else
        {
            foreach (var field in fields)
                DrawField(graphics, labelFont, bodyFont, field.Label, field.Value, contentX, ref y,
                    contentWidth, layout.LabelWidth, layout.LineHeight);
        }
    }

    private static void DrawAcrobatFields(
        XGraphics graphics,
        XFont font,
        IReadOnlyList<PdfStampField> fields,
        double x,
        ref double y,
        double width,
        double lineHeight)
    {
        graphics.DrawString(fields[0].Label.TrimEnd(), font, XBrushes.Black,
            new XRect(x, y, width, lineHeight), XStringFormats.TopLeft);
        y += lineHeight;
        graphics.DrawString(FitText(graphics, fields[0].Value, font, width), font, XBrushes.Black,
            new XRect(x, y, width, lineHeight), XStringFormats.TopLeft);
        y += lineHeight;
        foreach (var field in fields.Skip(1))
        {
            var line = field.Label + field.Value;
            graphics.DrawString(FitText(graphics, line, font, width), font, XBrushes.Black,
                new XRect(x, y, width, lineHeight), XStringFormats.TopLeft);
            y += lineHeight;
        }
    }

    private double DrawLogo(XGraphics graphics, XRect inner)
    {
        if (string.IsNullOrWhiteSpace(_settings.LogoPath) || !File.Exists(_settings.LogoPath)) return 0;
        try
        {
            using var image = XImage.FromFile(_settings.LogoPath);
            var maxWidth = inner.Width * 0.24;
            var maxHeight = inner.Height * 0.78;
            var scale = Math.Min(maxWidth / image.PixelWidth, maxHeight / image.PixelHeight);
            var width = image.PixelWidth * scale;
            var height = image.PixelHeight * scale;
            var x = inner.X + (maxWidth - width) / 2 + 2;
            var y = inner.Y + (inner.Height - height) / 2;
            graphics.DrawImage(image, x, y, width, height);
            return maxWidth;
        }
        catch
        {
            return 0;
        }
    }

    private static void DrawField(
        XGraphics graphics,
        XFont labelFont,
        XFont valueFont,
        string label,
        string value,
        double x,
        ref double y,
        double width,
        double labelWidth,
        double lineHeight)
    {
        graphics.DrawString(label, labelFont, XBrushes.Black,
            new XRect(x, y, labelWidth, lineHeight), XStringFormats.TopLeft);
        var available = Math.Max(1, width - labelWidth);
        var fitted = FitText(graphics, value, valueFont, available);
        graphics.DrawString(fitted, valueFont, XBrushes.Black,
            new XRect(x + labelWidth, y, available, lineHeight), XStringFormats.TopLeft);
        y += lineHeight;
    }

    private static string FitText(XGraphics graphics, string value, XFont font, double width)
    {
        if (graphics.MeasureString(value, font).Width <= width) return value;
        const string suffix = "…";
        var low = 0;
        var high = value.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (graphics.MeasureString(value[..middle] + suffix, font).Width <= width) low = middle;
            else high = middle - 1;
        }
        return value[..low] + suffix;
    }
}
