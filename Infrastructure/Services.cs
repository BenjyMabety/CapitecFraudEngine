using CapitecFraudEngine.Domain;
using Dapper;
using MySqlConnector;
using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CapitecFraudEngine.Infrastructure;

public static class DummyFileGenerator
{
    public static string Generate(string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        var filePath = Path.Combine(targetDir, $"transactions_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");

        var rows = new[]
        {
            "TransactionId,AccountNumber,AccountName,TransactionDate,Amount,TransactionType,Merchant",
            $"TX{Guid.NewGuid().ToString()[..8]},ACC1001,Ben Mbete,2026-09-07T10:00:00Z,500.00,Deposit,Salary",
            $"TX{Guid.NewGuid().ToString()[..8]},ACC1001,Ben Mbete,2026-09-07T10:05:00Z,-85000.00,Withdrawal,Crypto Exchange",
            $"TX{Guid.NewGuid().ToString()[..8]},ACC1002,Sarah Connor,2026-09-07T10:10:00Z,-250.00,Withdrawal,Coffee Shop"
        };

        File.WriteAllLines(filePath, rows);
        return filePath;
    }
}

public static class SecurityUtils
{
    public static string HashPasswordSha256(string rawPassword)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawPassword));
        var builder = new StringBuilder();

        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }
}

public class ArchiveService
{
    public void ZipAndArchive(string sourceFilePath, string archiveDir)
    {
        Directory.CreateDirectory(archiveDir);
        var fileName = Path.GetFileName(sourceFilePath);
        var zipPath = Path.Combine(archiveDir, $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(sourceFilePath, fileName);
        }

        File.Delete(sourceFilePath);
    }
}

public class DeletedArchivesService : BackgroundService
{
    private readonly string _archiveDir;
    private readonly TimeSpan _retentionPeriod;
    private readonly ILogger<DeletedArchivesService> _logger;

    public DeletedArchivesService(IConfiguration configuration, ILogger<DeletedArchivesService> logger)
    {
        _logger = logger;
        _archiveDir = Path.Combine(Directory.GetCurrentDirectory(), "data", "archive");
        Directory.CreateDirectory(_archiveDir);

        // Read configuration with default falling back to 24 hours for assessment
        var retentionHours = configuration.GetValue<double>("ArchiveRetentionPeriodHours", 24.0);
        _retentionPeriod = TimeSpan.FromHours(retentionHours);

        _logger.LogInformation("DeletedArchivesService initialized with retention period of {Hours} hours.", retentionHours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CleanupExpiredArchives();

            // Run check periodically based on retention setting (capped to a minimum of 1 minute)
            var delayTime = _retentionPeriod < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : _retentionPeriod;
            await Task.Delay(delayTime, stoppingToken);
        }
    }

    private void CleanupExpiredArchives()
    {
        try
        {
            if (!Directory.Exists(_archiveDir)) return;

            var archiveFiles = Directory.GetFiles(_archiveDir, "*.zip");
            var thresholdDate = DateTime.UtcNow.Subtract(_retentionPeriod);

            foreach (var filePath in archiveFiles)
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.LastWriteTimeUtc < thresholdDate)
                {
                    _logger.LogInformation("Deleting expired archive file: {FileName} (Last Modified: {LastModifiedUtc})",
                        fileInfo.Name, fileInfo.LastWriteTimeUtc);

                    File.Delete(filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during archive cleanup processing.");
        }
    }
}

public class FileCollectorService : BackgroundService
{
    private readonly string _collectorDir;
    private readonly string _archiveDir;
    private readonly string _connectionString;
    private readonly FraudEvaluationEngine _fraudEngine;
    private readonly ArchiveService _archiveService;
    private readonly ILogger<FileCollectorService> _logger;

    public FileCollectorService(
        IConfiguration configuration,
        FraudEvaluationEngine fraudEngine,
        ArchiveService archiveService,
        ILogger<FileCollectorService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        _fraudEngine = fraudEngine;
        _archiveService = archiveService;
        _logger = logger;

        _collectorDir = Path.Combine(Directory.GetCurrentDirectory(), "data", "collector");
        _archiveDir = Path.Combine(Directory.GetCurrentDirectory(), "data", "archive");

        Directory.CreateDirectory(_collectorDir);
        Directory.CreateDirectory(_archiveDir);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Watcher active on directory: {Dir}", _collectorDir);

        while (!stoppingToken.IsCancellationRequested)
        {
            var files = Directory.GetFiles(_collectorDir, "*.csv");
            foreach (var filePath in files)
            {
                await ProcessFileAsync(filePath, stoppingToken);
            }

            await Task.Delay(2000, stoppingToken);
        }
    }

    private async Task ProcessFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(filePath);
        _logger.LogInformation("Ingesting file: {FileName}", fileName);

        using var db = new MySqlConnection(_connectionString);

        try
        {
            // 1. File Hash & Duplicate Check
            using var fileStream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(fileStream, cancellationToken);
            var fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            var exists = await db.ExecuteScalarAsync<bool>(
                "SELECT COUNT(1) FROM ProcessedFiles WHERE FileHash = @FileHash", new { FileHash = fileHash });

            if (exists)
            {
                _logger.LogWarning("Duplicate file detected: {FileName}. Deleting.", fileName);
                fileStream.Close();
                File.Delete(filePath);
                return;
            }

            // 2. Structural Integrity Check
            fileStream.Position = 0;
            using var reader = new StreamReader(fileStream);
            var headerLine = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(headerLine) || !headerLine.StartsWith("TransactionId,AccountNumber"))
            {
                _logger.LogError("Structural Integrity Failure: {FileName}", fileName);
                fileStream.Close();
                File.Delete(filePath);
                return;
            }

            // 3. Extract Records
            var records = new List<TransactionRecord>();
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                var parts = line.Split(',');
                if (parts.Length < 7) continue;

                records.Add(new TransactionRecord
                {
                    TransactionId = parts[0],
                    AccountNumber = parts[1],
                    AccountName = parts[2],
                    TransactionDate = DateTime.Parse(parts[3], CultureInfo.InvariantCulture),
                    Amount = decimal.Parse(parts[4], CultureInfo.InvariantCulture),
                    TransactionType = TransactionTypeConstants.FromString(parts[5]),
                    Merchant = parts[6]
                });
            }

            // Save File Metadata using Raw SQL (Dapper)
            var fileId = await db.QuerySingleAsync<int>(
                @"INSERT INTO ProcessedFiles (FileName, FileHash, RecordCount) 
                  VALUES (@FileName, @FileHash, @RecordCount);
                  SELECT LAST_INSERT_ID();",
                new { FileName = fileName, FileHash = fileHash, RecordCount = records.Count });

            // Batch Insert Transactions
            foreach (var r in records)
            {
                r.FileId = fileId;
                await db.ExecuteAsync(
                    @"INSERT INTO Transactions (TransactionId, FileId, AccountNumber, AccountName, TransactionDate, Amount, TransactionType, Merchant)
                      VALUES (@TransactionId, @FileId, @AccountNumber, @AccountName, @TransactionDate, @Amount, @TransactionType, @Merchant)", r);
            }

            // 4. Evaluate Fraud Rules
            // 4. Evaluate Fraud Rules
            var activeRules = (await db.QueryAsync<FraudRule>("SELECT * FROM FraudRules WHERE IsActive = TRUE")).ToList();

            foreach (var record in records)
            {
                var brokenRuleIds = _fraudEngine.EvaluateRecord(record, activeRules);
                foreach (var ruleId in brokenRuleIds)
                {
                    // 1. Get the matching rule object from activeRules
                    var matchedRule = activeRules.FirstOrDefault(r => r.RuleId == ruleId);
                    var ruleName = matchedRule?.RuleName ?? "Unknown Rule";

                    // 2. Save alert to DB
                    await db.ExecuteAsync(
                        "INSERT INTO FraudAlerts (TransactionId, RuleId) VALUES (@TransactionId, @RuleId)",
                        new { TransactionId = record.TransactionId, RuleId = ruleId });

                    // 3. Pipe fraud alert to ILogger log file
                    _logger.LogWarning("[FRAUD ALERT RAISED] Rule Broken: {RuleName} (RuleID: {RuleId}) | Account: {AccountNumber} | TxID: {TransactionId} | Amount: {Amount}",
                        ruleName, ruleId, record.AccountNumber, record.TransactionId, record.Amount);
                }
            }

            // 5. Zip and Archive
            fileStream.Close();
            _archiveService.ZipAndArchive(filePath, _archiveDir);
            _logger.LogInformation("Successfully ingested {Count} records from {FileName}", records.Count, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file {FileName}", fileName);
        }
    }
}