using System.Text;
using OfficeIMO.Pdf;

namespace CryptoSigTool;

internal static class IncrementalPdfSignatureBuilder
{
    private const int MinimumAppearanceReservationCharacters = 32_768;
    private const int MaximumAppearanceReservationCharacters = 650_000;

    public static (PdfExternalSignaturePreparation Preparation, byte[] PreparedPdf) Prepare(
        byte[] sourcePdf,
        PdfExternalSignatureOptions options,
        byte[]? appearanceContent)
    {
        string? marker = null;
        if (options.VisibleAppearance is not null)
        {
            marker = "CST_APPEARANCE_" + Guid.NewGuid().ToString("N") + "_";
            var reservation = Math.Clamp(
                (appearanceContent?.Length ?? 0) + 2_048,
                MinimumAppearanceReservationCharacters,
                MaximumAppearanceReservationCharacters);
            options.VisibleAppearance.Text = marker + new string('X', reservation - marker.Length);
        }

        var document = OfficeIMO.Pdf.PdfDocument.Open(sourcePdf);
        var preparation = document.Security.PrepareExternalSignature(options);
        var prepared = preparation.PreparedPdf;
        if (!prepared.AsSpan(0, sourcePdf.Length).SequenceEqual(sourcePdf))
            throw new InvalidDataException("Инкрементальная подготовка изменила байты существующей редакции PDF.");

        if (marker is not null && appearanceContent is not null)
            PatchAppearance(prepared, marker, appearanceContent);
        return (preparation, prepared);
    }

    public static byte[] GetSignedContent(byte[] preparedPdf, IReadOnlyList<long> byteRange)
    {
        if (byteRange.Count != 4)
            throw new InvalidDataException("Ожидался четырёхэлементный ByteRange новой PDF-подписи.");
        var firstOffset = checked((int)byteRange[0]);
        var firstLength = checked((int)byteRange[1]);
        var secondOffset = checked((int)byteRange[2]);
        var secondLength = checked((int)byteRange[3]);
        var result = new byte[checked(firstLength + secondLength)];
        Buffer.BlockCopy(preparedPdf, firstOffset, result, 0, firstLength);
        Buffer.BlockCopy(preparedPdf, secondOffset, result, firstLength, secondLength);
        return result;
    }

    public static byte[] Complete(byte[] sourcePdf, byte[] preparedPdf, PdfExternalSignaturePreparation preparation, byte[] signature)
    {
        var hex = Encoding.ASCII.GetBytes(Convert.ToHexString(signature));
        if (hex.Length > preparation.ContentsHexLength)
            throw new InvalidDataException($"Подпись занимает {signature.Length} байт, зарезервировано {preparation.ReservedSignatureContentsBytes} байт.");

        var completed = (byte[])preparedPdf.Clone();
        completed.AsSpan(preparation.ContentsHexOffset, preparation.ContentsHexLength).Fill((byte)'0');
        hex.CopyTo(completed.AsSpan(preparation.ContentsHexOffset));
        if (!completed.AsSpan(0, sourcePdf.Length).SequenceEqual(sourcePdf))
            throw new InvalidDataException("При добавлении новой подписи были изменены байты предыдущей редакции PDF.");
        return completed;
    }

    private static void PatchAppearance(byte[] pdf, string marker, byte[] content)
    {
        var markerIndex = IndexOf(pdf, Encoding.ASCII.GetBytes(marker), 0);
        if (markerIndex < 0)
            throw new InvalidDataException("Не найден резерв видимого штампа новой PDF-подписи.");

        var streamCrlf = Encoding.ASCII.GetBytes("stream\r\n");
        var streamLf = Encoding.ASCII.GetBytes("stream\n");
        var streamStart = LastIndexOf(pdf, streamCrlf, markerIndex);
        var delimiterLength = streamCrlf.Length;
        var alternative = LastIndexOf(pdf, streamLf, markerIndex);
        if (alternative > streamStart)
        {
            streamStart = alternative;
            delimiterLength = streamLf.Length;
        }
        if (streamStart < 0)
            throw new InvalidDataException("Не найден поток видимого штампа PDF-подписи.");
        var contentStart = streamStart + delimiterLength;
        var contentEnd = IndexOf(pdf, Encoding.ASCII.GetBytes("endstream"), markerIndex);
        if (contentEnd <= contentStart)
            throw new InvalidDataException("Некорректный поток видимого штампа PDF-подписи.");

        var capacity = contentEnd - contentStart;
        if (content.Length > capacity)
            throw new InvalidDataException($"Векторный штамп требует {content.Length} байт, доступно {capacity} байт.");
        pdf.AsSpan(contentStart, capacity).Fill((byte)' ');
        content.CopyTo(pdf.AsSpan(contentStart));
    }

    private static int IndexOf(byte[] source, byte[] value, int start)
    {
        var index = source.AsSpan(start).IndexOf(value);
        return index < 0 ? -1 : start + index;
    }

    private static int LastIndexOf(byte[] source, byte[] value, int before)
    {
        if (before <= 0) return -1;
        return source.AsSpan(0, before).LastIndexOf(value);
    }
}
