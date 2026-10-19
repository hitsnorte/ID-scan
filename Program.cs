using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.ServiceProcess;
using System.Text.Json;
using pt.portugal.eid;

namespace LeitorCCService;

public class DatabaseWindowsService : ServiceBase
{
    private Thread? workerThread;
    private bool running = true;

    public DatabaseWindowsService()
    {
        ServiceName = "LeitorCCService";
        CanStop = true;
        AutoLog = true;
    }

    protected override void OnStart(string[] args)
    {
        Log("SERVICE STARTING");
        workerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "LeitorCCWorker"
        };
        workerThread.Start();
        Log("SERVICE STARTED");
    }

    protected override void OnStop()
    {
        running = false;
        workerThread?.Join();
        Log("SERVICE STOPPED");
    }

    private void WorkerLoop()
    {
        try
        {
            PTEID_ReaderSet.initSDK();
            Log("SDK INITIALIZED");

            var ptReader = new PtCardReader();
            var esReader = new EsCardReader();

            while (running)
            {
                try
                {
                    dynamic reader = PTEID_ReaderSet.instance().getReader();

                    if (!reader.isCardPresent())
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    Log("CARTÃO DETETADO");
                    CardData? cardData = null;

                    try
                    {
                        cardData = ptReader.Read(reader);
                        Log("CARTÃO PT LIDO");
                    }
                    catch (Exception ex)
                    {
                        Log($"WARNING: Leitura PT falhou. Tentando espanhol: {ex.Message}");
                    }

                    if (cardData is null)
                    {
                        try
                        {
                            cardData = esReader.Read();
                            Log("CARTÃO ESPANHÓL LIDO");
                        }
                        catch (Exception ex)
                        {
                            Log($"ERROR: Leitura espanhola falhou: {ex}");
                        }
                    }

                    if (cardData is not null)
                    {
                        Log(JsonSerializer.Serialize(cardData));
                        SaveToDatabase(cardData);
                    }
                    else
                    {
                        Log("NÃO FOI POSSÍVEL LER O CARTÃO NOS LEITORES DISPONÍVEIS");
                    }

                    WaitUntilCardRemoved(reader);
                    Log("CARTÃO REMOVIDO");
                }
                catch (PTEID_ExNoReader ex)
                {
                    Log($"WARNING: Leitor desconectado: {ex.Message}");

                    try
                    {
                        PTEID_ReaderSet.releaseSDK();
                        Thread.Sleep(1000);
                        PTEID_ReaderSet.initSDK();
                        Log("SDK REINITIALIZED");
                    }
                    catch (Exception sdkEx)
                    {
                        Log($"ERROR REINITIALIZING SDK: {sdkEx}");
                        Thread.Sleep(3000);
                    }
                }
                catch (Exception ex)
                {
                    Log($"ERROR: {ex}");
                    Thread.Sleep(1000);
                }
            }
        }
        finally
        {
            try
            {
                PTEID_ReaderSet.releaseSDK();
                Log("SDK RELEASED");
            }
            catch (Exception ex)
            {
                Log($"ERROR RELEASE SDK: {ex}");
            }
        }
    }

    private static void WaitUntilCardRemoved(dynamic reader)
    {
        while (true)
        {
            try
            {
                if (!reader.isCardPresent())
                {
                    return;
                }
            }
            catch (PTEID_ExNoReader)
            {
                return;
            }

            Thread.Sleep(500);
        }
    }

    private static void SaveToDatabase(CardData card)
    {
        try
        {
            string connectionString = LoadConnectionString();
            using var conn = new SqlConnection(connectionString);
            conn.Open();

            string query = @"
                INSERT INTO CCReaderData
                (Name, LastName, DocumentNr, FiscalNr, BirthDate, Nationality,
                 Gender, ExpirationDate, Country, District, Municipality,
                 Street, PostalCode, Locality)
                VALUES
                (@Name, @LastName, @DocumentNr, @FiscalNr, @BirthDate, @Nationality,
                 @Gender, @ExpirationDate, @Country, @District, @Municipality,
                 @Street, @PostalCode, @Locality)";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = NormalizeName(card.Name);
            cmd.Parameters.Add("@LastName", SqlDbType.NVarChar, 100).Value = NormalizeName(card.LastName);
            cmd.Parameters.Add("@DocumentNr", SqlDbType.NVarChar, 50).Value = card.DocumentNr;
            cmd.Parameters.Add("@FiscalNr", SqlDbType.NVarChar, 50).Value = card.TaxNumber;
            cmd.Parameters.Add("@BirthDate", SqlDbType.DateTime).Value = ParseDate(card.BirthDate);
            cmd.Parameters.Add("@Nationality", SqlDbType.NVarChar, 50).Value = card.Nationality;
            cmd.Parameters.Add("@Gender", SqlDbType.NVarChar, 10).Value = card.Gender;
            cmd.Parameters.Add("@ExpirationDate", SqlDbType.DateTime).Value = ParseDate(card.ExpirationDate);
            cmd.Parameters.Add("@Country", SqlDbType.NVarChar, 10).Value = NormalizeName(card.Country);
            cmd.Parameters.Add("@District", SqlDbType.NVarChar, 100).Value = NormalizeName(card.District);
            cmd.Parameters.Add("@Municipality", SqlDbType.NVarChar, 100).Value = NormalizeName(card.Municipality);
            cmd.Parameters.Add("@Street", SqlDbType.NVarChar, 250).Value = NormalizeName(card.Street);
            cmd.Parameters.Add("@PostalCode", SqlDbType.NVarChar, 20).Value = card.PostalCode;
            cmd.Parameters.Add("@Locality", SqlDbType.NVarChar, 100).Value = NormalizeName(card.Locality);

            cmd.ExecuteNonQuery();
            Log("DADOS INSERIDOS NA BASE DE DADOS COM SUCESSO");
        }
        catch (Exception ex)
        {
            Log($"ERRO AO INSERIR NA BASE DE DADOS: {ex}");
        }
    }

    private static object ParseDate(string date)
    {
        return DateTime.TryParse(date, out DateTime parsedDate)
            ? parsedDate.Date
            : DBNull.Value;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }

    private static string NormalizeName(string value)
    {
        string normalized = Normalize(value);
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }

    private static string LoadConnectionString()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connectionString.txt");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("connectionString.txt não encontrado.");
        }

        return File.ReadAllText(path).Trim();
    }

    private static void Log(string message)
    {
        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        string logFile = Path.Combine(logDir, $"{DateTime.Now:dd-MM-yyyy}.log");
        File.AppendAllText(
            logFile,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}{Environment.NewLine}");

        try
        {
            foreach (string file in Directory.GetFiles(logDir, "*.log"))
            {
                DateTime creationTime = File.GetCreationTime(file);
                if ((DateTime.Now - creationTime).TotalDays > 7)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText(
                logFile,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | ERRO AO LIMPAR LOGS ANTIGOS: {ex}{Environment.NewLine}");
        }
    }
}

internal static class Program
{
    private static void Main()
    {
        ServiceBase.Run(new ServiceBase[] { new DatabaseWindowsService() });
    }
}
