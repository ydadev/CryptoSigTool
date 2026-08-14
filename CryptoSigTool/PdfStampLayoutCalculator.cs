using System.Drawing.Drawing2D;

namespace CryptoSigTool;

internal sealed record PdfStampField(string Label, string Value);

internal sealed record PdfStampTextLayout(
    float BodySize,
    float HeaderSize,
    float HeaderHeight,
    float LineHeight,
    float Gap,
    float StartOffset,
    float BodyStartOffset,
    float LabelWidth);

internal static class PdfStampLayoutCalculator
{
    public static PdfStampTextLayout Calculate(
        PdfStampStyle style,
        float innerHeight,
        float contentWidth,
        IReadOnlyList<PdfStampField> fields)
    {
        using var family = new FontFamily("Arial");
        var labelStyle = style.BoldLabels ? FontStyle.Bold : FontStyle.Regular;
        var preferredBody = Math.Clamp(innerHeight / 12.2F, 3.2F, 12F);
        var widestLabelAtUnitSize = fields.Max(field =>
            MeasureText(field.Label, family, labelStyle, 10F) / 10F);
        var widestValueAtUnitSize = fields.Max(field =>
            MeasureText(field.Value, family, FontStyle.Regular, 10F) / 10F);
        var bodyLineCount = fields.Count;
        float widestBodyLineAtUnitSize;
        if (style.AcrobatFieldLayout)
        {
            bodyLineCount++;
            var lineWidths = new List<float>
            {
                MeasureText(fields[0].Label.TrimEnd(), family, FontStyle.Regular, 10F) / 10F,
                MeasureText(fields[0].Value, family, FontStyle.Regular, 10F) / 10F
            };
            lineWidths.AddRange(fields.Skip(1).Select(field =>
                MeasureText(field.Label + field.Value, family, FontStyle.Regular, 10F) / 10F));
            widestBodyLineAtUnitSize = lineWidths.Max();
        }
        else
        {
            widestBodyLineAtUnitSize = widestLabelAtUnitSize + widestValueAtUnitSize + 0.55F;
        }
        var widthLimitedBody = contentWidth * 0.93F / Math.Max(1F, widestBodyLineAtUnitSize);
        var bodySize = Math.Clamp(Math.Min(preferredBody, widthLimitedBody), 2.1F, 12F);

        var headerStyle = style.BoldHeader ? FontStyle.Bold : FontStyle.Regular;
        var headerRatio = style.CenteredHeader ? 1.55F : 1.24F;
        var preferredHeader = bodySize * headerRatio;
        var widestHeaderAtUnitSize = Math.Max(
            MeasureText("ДОКУМЕНТ ПОДПИСАН", family, headerStyle, 10F),
            MeasureText("ЭЛЕКТРОННОЙ ПОДПИСЬЮ", family, headerStyle, 10F)) / 10F;
        var widthLimitedHeader = contentWidth * 0.94F / Math.Max(1F, widestHeaderAtUnitSize);
        var headerSize = Math.Clamp(Math.Min(preferredHeader, widthLimitedHeader), 2.6F, 15F);

        var gapFactor = style.AcrobatFieldLayout ? 4F : style.CenteredHeader ? 1.65F : 0.35F;
        var headerHeight = headerSize * 1.18F;
        var lineHeight = bodySize * 1.3F;
        var gap = bodySize * gapFactor;
        var totalHeight = headerHeight * 2 + gap + lineHeight * bodyLineCount;
        var availableHeight = innerHeight * 0.88F;
        if (totalHeight > availableHeight)
        {
            var scale = Math.Clamp(availableHeight / totalHeight, 0.25F, 1F);
            bodySize *= scale;
            headerSize *= scale;
            headerHeight *= scale;
            lineHeight *= scale;
            gap *= scale;
            totalHeight = headerHeight * 2 + gap + lineHeight * bodyLineCount;
        }

        var maximumLabel = fields.Max(field => MeasureText(field.Label, family, labelStyle, bodySize));
        var labelWidth = Math.Min(contentWidth * 0.48F, maximumLabel + bodySize * 0.45F);
        var startOffset = style.AcrobatFieldLayout
            ? innerHeight * 0.15F
            : Math.Max(0, (innerHeight - totalHeight) / 2);
        var bodyStartOffset = style.AcrobatFieldLayout
            ? innerHeight * 0.56F
            : startOffset + headerHeight * 2 + gap;
        if (style.AcrobatFieldLayout)
        {
            var availableBodyHeight = Math.Max(1, innerHeight * 0.9F - bodyStartOffset);
            if (lineHeight * bodyLineCount > availableBodyHeight)
            {
                var bodyScale = Math.Clamp(availableBodyHeight / (lineHeight * bodyLineCount), 0.25F, 1F);
                bodySize *= bodyScale;
                lineHeight *= bodyScale;
            }
        }
        return new PdfStampTextLayout(
            bodySize,
            headerSize,
            headerHeight,
            lineHeight,
            gap,
            startOffset,
            bodyStartOffset,
            labelWidth);
    }

    public static float MeasureText(
        string value,
        FontFamily family,
        FontStyle style,
        float emSize)
    {
        using var path = new GraphicsPath();
        path.AddString(value, family, (int)style, emSize, PointF.Empty, StringFormat.GenericTypographic);
        return path.GetBounds().Width;
    }
}
