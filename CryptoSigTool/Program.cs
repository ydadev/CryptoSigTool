namespace CryptoSigTool;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0].Equals("--ui-smoke", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            using var form = new MainForm();
            form.CreateControl();
            Console.WriteLine("OK ui tabs=" + form.TabOrderForTest);
            return 0;
        }

        if (args.Length == 2 && args[0].Equals("--inspect", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var result = CmsMerger.Inspect(args[1]);
                Console.WriteLine($"detached={result.Detached} content={result.ContentType} certificates={result.CertificateCount} signers={result.Signers.Count}");
                foreach (var signer in result.Signers)
                    Console.WriteLine($"#{signer.Number} {signer.DisplayName} | time={signer.SigningTime:O} | digest={signer.DigestAlgorithm} | signature={signer.SignatureAlgorithm} | timestamp={signer.TimestampTime:O}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 1 && args[0].Equals("--scan-containers", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var cryptoPro = new CryptoProService();
                var result = cryptoPro.GetMissingContainerCertificatesAsync(Console.WriteLine).GetAwaiter().GetResult();
                Console.WriteLine($"missing={result.Count}");
                foreach (var certificate in result)
                    Console.WriteLine($"{certificate.DisplayName} | {certificate.KeyTypeDisplay} | {certificate.ContainerName} | {certificate.Thumbprint}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 4 && (args[0].Equals("--merge", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--merge64", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var result = CmsMerger.Merge(new[] { args[2], args[3] }, args[1], args[0].Equals("--merge64", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"OK signers={result.Signers} certificates={result.Certificates} detached={result.Detached}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        if (args.Length == 4 && args[0].Equals("--attach", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var result = CmsMerger.Merge(new[] { args[3], args[3] }, args[1], false, args[2]);
                Console.WriteLine($"OK signers={result.Signers} certificates={result.Certificates} detached={result.Detached}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
