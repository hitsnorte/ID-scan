using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace LeitorCCService;

public sealed class EsCardReader
{
    private const string DniMiddlewarePath = @"C:\Windows\System32\DNIe_P11_x64.dll";

    public CardData? Read()
    {
        if (!File.Exists(DniMiddlewarePath))
        {
            return null;
        }

        try
        {
            var factories = new Pkcs11InteropFactories();
            using var pkcs11 = factories.Pkcs11LibraryFactory.LoadPkcs11Library(
                factories,
                DniMiddlewarePath,
                AppType.MultiThreaded);

            foreach (var slot in pkcs11.GetSlotList(SlotsType.WithTokenPresent))
            {
                using var session = slot.OpenSession(SessionType.ReadOnly);
                var certificateTemplate = new List<IObjectAttribute>
                {
                    session.Factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE)
                };

                foreach (var certificateObject in session.FindAllObjects(certificateTemplate))
                {
                    var certificateAttributes = session.GetAttributeValue(
                        certificateObject,
                        new List<CKA> { CKA.CKA_VALUE });
                    byte[]? certificateBytes = certificateAttributes[0].GetValueAsByteArray();

                    if (certificateBytes is null || certificateBytes.Length == 0)
                    {
                        continue;
                    }

                    using var certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
                    string subject = certificate.Subject ?? string.Empty;
                    string issuer = certificate.Issuer ?? string.Empty;

                    if (!subject.Contains("DNIe", StringComparison.OrdinalIgnoreCase)
                        && !issuer.Contains("DIRECCION GENERAL DE LA POLICIA", StringComparison.OrdinalIgnoreCase)
                        && !issuer.Contains("DGP", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Match dniMatch = Regex.Match(
                        subject,
                        @"(?<![A-Z0-9])[XYZ]?\d{7,8}[A-Z](?![A-Z0-9])",
                        RegexOptions.IgnoreCase);

                    if (!dniMatch.Success)
                    {
                        continue;
                    }

                    string name = certificate.GetNameInfo(X509NameType.SimpleName, false);
                    name = Regex.Replace(
                        name ?? string.Empty,
                        @"\s*\((AUTENTICACIÓN|AUTENTICACION|FIRMA)\)\s*$",
                        string.Empty,
                        RegexOptions.IgnoreCase).Trim();

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string[] nameParts = name.Split(',', 2, StringSplitOptions.TrimEntries);
                    string lastName = nameParts.Length == 2 ? ToTitleCase(nameParts[0]) : string.Empty;
                    string givenName = ToTitleCase(nameParts.Length == 2 ? nameParts[1] : nameParts[0]);

                    return new CardData(
                        givenName,
                        lastName,
                        dniMatch.Value.ToUpperInvariant(),
                        dniMatch.Value.ToUpperInvariant(),
                        string.Empty,
                        "ESP",
                        string.Empty,
                        certificate.NotAfter.Date.ToString("yyyy-MM-dd"),
                        "ES",
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Não foi possível ler o DNI espanhol: {ex.Message}");
        }

        return null;
    }

    private static string ToTitleCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        text = Regex.Replace(text.Trim(), @"\s+", " ");
        text = Regex.Replace(text, @"\s*,\s*", ", ");
        text = Regex.Replace(text, @"\s*-\s*", "-");

        string[] words = text.ToLowerInvariant().Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0)
            {
                continue;
            }

            string[] parts = words[i].Split('-', StringSplitOptions.None);
            for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            {
                if (parts[partIndex].Length > 0)
                {
                    parts[partIndex] = char.ToUpperInvariant(parts[partIndex][0])
                        + parts[partIndex][1..];
                }
            }

            words[i] = string.Join('-', parts);
        }

        return string.Join(" ", words);
    }
}
