using System.Runtime.InteropServices;
using PdfSharp.Pdf.Signatures;

namespace CryptoSigTool;

internal sealed class CadesComPdfSigner : IDigitalSigner
{
    private const int CapicomCurrentUserStore = 2;
    private const int CapicomLocalMachineStore = 1;
    private const int CapicomStoreOpenMaximumAllowed = 2;
    private const int CapicomCertificateFindSha1Hash = 0;
    private const int CapicomCertificateIncludeWholeChain = 1;
    private const int CadescomBase64ToBinary = 1;
    private const int CapicomEncodeBase64 = 0;
    private const int CadesBes = 0x01;
    private const int CadesT = 0x05;
    private const int CadesXLongType1 = 0x5D;

    private readonly CertificateItem _certificate;
    private readonly string _tspAddress;
    private readonly bool _addEvidence;

    public CadesComPdfSigner(CertificateItem certificate, string tspAddress, bool addEvidence)
    {
        _certificate = certificate;
        _tspAddress = tspAddress;
        _addEvidence = addEvidence;
    }

    public static bool IsAvailable =>
        Type.GetTypeFromProgID("CAdESCOM.CadesSignedData") is not null &&
        Type.GetTypeFromProgID("CAdESCOM.CPSigner") is not null &&
        Type.GetTypeFromProgID("CAdESCOM.Store") is not null;

    public string CertificateName => _certificate.DisplayName;

    public Task<int> GetSignatureSizeAsync() => Task.FromResult(_addEvidence ? 512 * 1024 : 128 * 1024);

    public async Task<byte[]> GetSignatureAsync(Stream stream)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("Компонент CryptoPro CAdESCOM не найден. Для TSP и доказательств подлинности установите совместимые компоненты CAdES/TSP/OCSP.");

        using var buffer = new MemoryStream();
        await PdfSignatureStream.CopySequentiallyAsync(stream, buffer);
        return Sign(buffer.ToArray());
    }

    private byte[] Sign(byte[] content)
    {
        object? storeObject = null;
        object? certificatesObject = null;
        object? certificateObject = null;
        object? signerObject = null;
        object? signedDataObject = null;
        try
        {
            var storeType = Type.GetTypeFromProgID("CAdESCOM.Store")
                ?? throw new InvalidOperationException("CAdESCOM.Store не зарегистрирован.");
            var signerType = Type.GetTypeFromProgID("CAdESCOM.CPSigner")
                ?? throw new InvalidOperationException("CAdESCOM.CPSigner не зарегистрирован.");
            var signedDataType = Type.GetTypeFromProgID("CAdESCOM.CadesSignedData")
                ?? throw new InvalidOperationException("CAdESCOM.CadesSignedData не зарегистрирован.");

            storeObject = Activator.CreateInstance(storeType)!;
            dynamic store = storeObject;
            var location = _certificate.StoreLocation == System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser
                ? CapicomCurrentUserStore
                : CapicomLocalMachineStore;
            store.Open(location, "My", CapicomStoreOpenMaximumAllowed);
            certificatesObject = store.Certificates.Find(CapicomCertificateFindSha1Hash, _certificate.Thumbprint, false);
            dynamic certificates = certificatesObject;
            if ((int)certificates.Count == 0)
                throw new InvalidOperationException("Выбранный сертификат не найден через интерфейс CryptoPro CAdESCOM.");

            certificateObject = certificates.Item(1);
            signerObject = Activator.CreateInstance(signerType)!;
            dynamic signer = signerObject;
            signer.Certificate = certificateObject;
            signer.Options = CapicomCertificateIncludeWholeChain;
            signer.CheckCertificate = true;
            signer.TSAAddress = _tspAddress;

            signedDataObject = Activator.CreateInstance(signedDataType)!;
            dynamic signedData = signedDataObject;
            signedData.ContentEncoding = CadescomBase64ToBinary;
            signedData.Content = Convert.ToBase64String(content);
            var cadesType = _addEvidence ? CadesXLongType1 : CadesT;
            string encoded = signedData.SignCades(signer, cadesType, true, CapicomEncodeBase64);
            return Convert.FromBase64String(encoded);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"CryptoPro CAdESCOM завершил операцию с ошибкой 0x{ex.HResult:X8}: {ex.Message}", ex);
        }
        finally
        {
            ReleaseCom(signedDataObject);
            ReleaseCom(signerObject);
            ReleaseCom(certificateObject);
            ReleaseCom(certificatesObject);
            if (storeObject is not null)
            {
                try { ((dynamic)storeObject).Close(); } catch { }
                ReleaseCom(storeObject);
            }
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }
}
