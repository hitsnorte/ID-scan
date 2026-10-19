using System.Globalization;
using pt.portugal.eid;

namespace LeitorCCService;

public sealed class PtCardReader
{
    public CardData Read(dynamic reader)
    {
        dynamic card = reader.getEIDCard();
        dynamic eid = card.getID();
        dynamic pins = card.getPins();
        dynamic addressPin = pins.getPinByPinRef(PTEID_Pin.ADDR_PIN);

        uint triesLeft = uint.MaxValue;
        bool pinOk = addressPin.verifyPin("0000", ref triesLeft, true);

        if (!pinOk)
        {
            throw new InvalidOperationException(
                $"PIN incorreto. Tentativas restantes: {triesLeft}");
        }

        object? address = null;
        try
        {
            address = card.getAddr();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Não foi possível obter morada: {ex.Message}");
        }

        string nationality = eid.getNationality();
        string country = string.IsNullOrWhiteSpace(GetAddressValue(address, "getCountryCode"))
            && nationality == "PRT"
            ? "PT"
            : GetAddressValue(address, "getCountryCode");

        return new CardData(
            NormalizeName(eid.getGivenName()),
            NormalizeName(eid.getSurname()),
            eid.getDocumentNumber(),
            eid.getTaxNo(),
            FormatDate(eid.getDateOfBirth()),
            nationality,
            eid.getGender(),
            FormatDate(eid.getValidityEndDate()),
            country,
            GetAddressValue(address, "getDistrict"),
            GetAddressValue(address, "getMunicipality"),
            BuildStreet(address),
            BuildPostalCode(address),
            NormalizeName(GetAddressValue(address, "getPostalLocality")));
    }

    private static string NormalizeName(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());
    }

    private static string GetAddressValue(object? address, string getterName)
    {
        if (address is null)
        {
            return string.Empty;
        }

        try
        {
            object? value = address.GetType().GetMethod(getterName)?.Invoke(address, null);
            return value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildStreet(object? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        try
        {
            string street = GetAddressValue(address, "getStreetName");
            string door = GetAddressValue(address, "getDoorNo");
            string floor = GetAddressValue(address, "getFloor");

            return string.Join(", ", new[] { street, door, floor }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildPostalCode(object? address)
    {
        if (address is null)
        {
            return string.Empty;
        }

        try
        {
            string zip4 = GetAddressValue(address, "getZip4");
            string zip3 = GetAddressValue(address, "getZip3");
            return $"{zip4}-{zip3}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatDate(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        try
        {
            return Convert.ToDateTime(value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return string.Empty;
        }
    }
}
