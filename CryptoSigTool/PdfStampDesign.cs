namespace CryptoSigTool;

internal enum PdfStampDesign
{
    AcrobatBlack,
    Fz63Green,
    OfficialBlue,
    MinimalBlack
}

internal sealed record PdfStampDesignItem(PdfStampDesign Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

internal sealed record PdfStampStyle(
    Color AccentColor,
    bool RoundedBorder,
    bool CenteredHeader,
    bool BoldHeader,
    bool BoldLabels,
    float BorderWidthFactor,
    bool AcrobatFieldLayout = false);

internal static class PdfStampDesignCatalog
{
    public static readonly IReadOnlyList<PdfStampDesignItem> Items = new[]
    {
        new PdfStampDesignItem(PdfStampDesign.AcrobatBlack, "Чёрный со скруглением — как Acrobat"),
        new PdfStampDesignItem(PdfStampDesign.Fz63Green, "Зелёный — 63-ФЗ"),
        new PdfStampDesignItem(PdfStampDesign.OfficialBlue, "Синий — официальный"),
        new PdfStampDesignItem(PdfStampDesign.MinimalBlack, "Чёрный — минималистичный")
    };

    public static PdfStampStyle GetStyle(PdfStampDesign design) => design switch
    {
        PdfStampDesign.AcrobatBlack => new PdfStampStyle(Color.Black, true, true, false, false, 0.018F, true),
        PdfStampDesign.Fz63Green => new PdfStampStyle(Color.FromArgb(0, 116, 74), false, false, true, true, 0.010F),
        PdfStampDesign.OfficialBlue => new PdfStampStyle(Color.FromArgb(22, 74, 128), true, false, true, true, 0.009F),
        PdfStampDesign.MinimalBlack => new PdfStampStyle(Color.Black, false, false, true, true, 0.006F),
        _ => throw new ArgumentOutOfRangeException(nameof(design))
    };
}
