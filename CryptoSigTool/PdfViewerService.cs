using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CryptoSigTool;

internal sealed class PdfViewerService : IDisposable
{
    private PdfDocument? _document;

    public uint PageCount => _document?.PageCount ?? 0;

    public async Task OpenAsync(string path)
    {
        DisposeDocument();
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        _document = await PdfDocument.LoadFromFileAsync(file);
    }

    public async Task<Image> RenderPageAsync(uint pageIndex, uint targetWidth = 1800)
    {
        var document = _document ?? throw new InvalidOperationException("PDF не открыт.");
        if (pageIndex >= document.PageCount) throw new ArgumentOutOfRangeException(nameof(pageIndex));

        using var page = document.GetPage(pageIndex);
        using var stream = new InMemoryRandomAccessStream();
        var options = new PdfPageRenderOptions { DestinationWidth = targetWidth };
        await page.RenderToStreamAsync(stream, options);
        if (stream.Size > int.MaxValue) throw new InvalidDataException("Отрисованная страница слишком велика.");

        var bytes = new byte[(int)stream.Size];
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
        }
        using var memory = new MemoryStream(bytes, writable: false);
        using var image = Image.FromStream(memory);
        return new Bitmap(image);
    }

    public void Dispose()
    {
        DisposeDocument();
        GC.SuppressFinalize(this);
    }

    private void DisposeDocument()
    {
        _document = null;
    }
}
