namespace CryptoSigTool;

internal static class PdfSignatureStream
{
    public static async Task CopySequentiallyAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        if (input.Length > int.MaxValue)
            throw new NotSupportedException("PDF files larger than 2 GiB cannot be signed.");

        var buffer = new byte[(int)input.Length];
        input.Position = 0;
        var offset = 0;
        while (offset < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = input.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
                throw new EndOfStreamException($"Expected {buffer.Length} PDF bytes but received {offset}.");
            offset += read;
        }

        await output.WriteAsync(buffer.AsMemory(), cancellationToken);
    }
}
