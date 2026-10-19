namespace LeitorCCService;

public sealed record CardData(
    string Name,
    string LastName,
    string DocumentNr,
    string TaxNumber,
    string BirthDate,
    string Nationality,
    string Gender,
    string ExpirationDate,
    string Country,
    string District,
    string Municipality,
    string Street,
    string PostalCode,
    string Locality);
