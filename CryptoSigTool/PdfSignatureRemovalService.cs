using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace CryptoSigTool;

internal sealed record EmbeddedPdfSignatureInfo(string FieldName, int? PageNumber);

internal sealed record PdfSignatureRemovalInspection(
    int PageCount,
    IReadOnlyList<EmbeddedPdfSignatureInfo> Signatures);

internal sealed record PdfSignatureRemovalResult(
    int RemovedSignatures,
    int PageCount,
    long OutputBytes);

internal static class PdfSignatureRemovalService
{
    public static PdfSignatureRemovalInspection Inspect(string inputPath)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("PDF-файл не найден.", inputPath);
        using var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
        var signatures = new Dictionary<string, EmbeddedPdfSignatureInfo>(StringComparer.Ordinal);

        for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
        {
            var annotations = document.Pages[pageIndex].Annotations;
            for (var annotationIndex = 0; annotationIndex < annotations.Count; annotationIndex++)
            {
                var annotation = annotations[annotationIndex];
                if (!IsSignatureField(annotation)) continue;
                var name = GetFieldName(annotation, signatures.Count + 1);
                signatures[name] = new EmbeddedPdfSignatureInfo(name, pageIndex + 1);
            }
        }

        var acroForm = document.Internals.Catalog.Elements.GetDictionary("/AcroForm");
        var fields = acroForm?.Elements.GetArray("/Fields");
        if (fields is not null)
            CollectSignatureFields(fields, null, signatures);

        return new PdfSignatureRemovalInspection(document.PageCount, signatures.Values.ToArray());
    }

    public static PdfSignatureRemovalResult RemoveAll(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("PDF-файл не найден.", inputPath);
        var inputFullPath = Path.GetFullPath(inputPath);
        var outputFullPath = Path.GetFullPath(outputPath);
        if (string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Исходный PDF и неподписанная копия должны быть разными файлами.");

        var before = Inspect(inputFullPath);
        if (before.Signatures.Count == 0)
            throw new InvalidOperationException("В PDF не обнаружены встроенные поля электронной подписи.");

        var outputDirectory = Path.GetDirectoryName(outputFullPath)
            ?? throw new InvalidOperationException("Не удалось определить папку результата.");
        Directory.CreateDirectory(outputDirectory);
        var temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(outputFullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var document = PdfReader.Open(inputFullPath, PdfDocumentOpenMode.Modify))
            {
                for (var pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
                {
                    var annotations = document.Pages[pageIndex].Annotations;
                    for (var annotationIndex = annotations.Count - 1; annotationIndex >= 0; annotationIndex--)
                    {
                        var annotation = annotations[annotationIndex];
                        if (IsSignatureField(annotation)) annotations.Remove(annotation);
                    }
                }

                var acroForm = document.Internals.Catalog.Elements.GetDictionary("/AcroForm");
                var fields = acroForm?.Elements.GetArray("/Fields");
                if (fields is not null)
                    RemoveSignatureFields(fields, null);

                if (acroForm is not null)
                {
                    acroForm.Elements.Remove("/SigFlags");
                    acroForm.Elements.Remove("/CO");
                    if (fields is null || fields.Elements.Count == 0)
                        document.Internals.Catalog.Elements.Remove("/AcroForm");
                }

                document.Internals.Catalog.Elements.Remove("/Perms");
                document.Internals.Catalog.Elements.Remove("/DSS");
                document.Save(temporaryPath);
            }

            var after = Inspect(temporaryPath);
            if (after.Signatures.Count != 0)
                throw new InvalidDataException($"После очистки в PDF осталось полей подписи: {after.Signatures.Count}.");
            if (after.PageCount != before.PageCount)
                throw new InvalidDataException("При очистке изменилось количество страниц PDF.");

            File.Move(temporaryPath, outputFullPath, true);
            return new PdfSignatureRemovalResult(
                before.Signatures.Count,
                after.PageCount,
                new FileInfo(outputFullPath).Length);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static int RemoveSignatureFields(PdfArray fields, string? inheritedFieldType)
    {
        var removed = 0;
        for (var index = fields.Elements.Count - 1; index >= 0; index--)
        {
            var field = fields.Elements.GetDictionary(index);
            if (field is null) continue;
            var ownFieldType = field.Elements.GetName("/FT");
            var effectiveFieldType = string.IsNullOrWhiteSpace(ownFieldType)
                ? inheritedFieldType
                : ownFieldType;
            if (string.Equals(effectiveFieldType, "/Sig", StringComparison.Ordinal))
            {
                fields.Elements.RemoveAt(index);
                removed++;
                continue;
            }

            var kids = field.Elements.GetArray("/Kids");
            if (kids is null) continue;
            removed += RemoveSignatureFields(kids, effectiveFieldType);
            if (kids.Elements.Count == 0)
            {
                field.Elements.Remove("/Kids");
                if (string.IsNullOrWhiteSpace(effectiveFieldType))
                    fields.Elements.RemoveAt(index);
            }
        }
        return removed;
    }

    private static void CollectSignatureFields(
        PdfArray fields,
        string? inheritedFieldType,
        IDictionary<string, EmbeddedPdfSignatureInfo> signatures)
    {
        for (var index = 0; index < fields.Elements.Count; index++)
        {
            var field = fields.Elements.GetDictionary(index);
            if (field is null) continue;
            var ownFieldType = field.Elements.GetName("/FT");
            var effectiveFieldType = string.IsNullOrWhiteSpace(ownFieldType)
                ? inheritedFieldType
                : ownFieldType;
            if (string.Equals(effectiveFieldType, "/Sig", StringComparison.Ordinal))
            {
                var name = GetFieldName(field, signatures.Count + 1);
                if (!signatures.ContainsKey(name))
                    signatures[name] = new EmbeddedPdfSignatureInfo(name, null);
            }

            var kids = field.Elements.GetArray("/Kids");
            if (kids is not null)
                CollectSignatureFields(kids, effectiveFieldType, signatures);
        }
    }

    private static bool IsSignatureField(PdfDictionary dictionary)
    {
        var current = dictionary;
        for (var depth = 0; depth < 64; depth++)
        {
            var fieldType = current.Elements.GetName("/FT");
            if (!string.IsNullOrWhiteSpace(fieldType))
                return string.Equals(fieldType, "/Sig", StringComparison.Ordinal);
            var parent = current.Elements.GetDictionary("/Parent");
            if (parent is null) return false;
            current = parent;
        }
        throw new InvalidDataException("Обнаружен циклический граф полей PDF.");
    }

    private static string GetFieldName(PdfDictionary dictionary, int fallbackNumber)
    {
        var parts = new Stack<string>();
        var current = dictionary;
        for (var depth = 0; depth < 64; depth++)
        {
            var part = current.Elements.GetString("/T");
            if (!string.IsNullOrWhiteSpace(part)) parts.Push(part);
            var parent = current.Elements.GetDictionary("/Parent");
            if (parent is null) break;
            current = parent;
        }
        return parts.Count == 0 ? $"Подпись {fallbackNumber}" : string.Join('.', parts);
    }
}
