using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Infrastructure;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed record DatabaseBackupResult(string FilePath, long FileSize, DateTimeOffset CreatedAt);

public sealed class DatabaseBackupService(
    IDbContextFactory<ApplicationDbContext> factory,
    IConfiguration configuration,
    CurrentUserService currentUser,
    ILogger<DatabaseBackupService> logger)
{
    public async Task<DatabaseBackupResult> CreateAsync(CancellationToken cancellationToken = default)
    {
        await currentUser.EnsureAdministratorAsync();
        var actor = await currentUser.GetAsync();

        var configuredDirectory = configuration["DatabaseBackup:Directory"];
        var backupDirectory = !string.IsNullOrWhiteSpace(configuredDirectory)
            ? Environment.ExpandEnvironmentVariables(configuredDirectory)
            : RuntimePaths.BackupsDirectory;

        Directory.CreateDirectory(backupDirectory);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("سلسلة اتصال قاعدة البيانات غير متوفرة.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new InvalidOperationException("اسم قاعدة البيانات غير موجود في سلسلة الاتصال.");

        var safeDatabaseName = string.Concat(databaseName.Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-'));
        if (string.IsNullOrWhiteSpace(safeDatabaseName))
            safeDatabaseName = "QatFarmDb";

        var fileName = $"{safeDatabaseName}_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
        var filePath = Path.GetFullPath(Path.Combine(backupDirectory, fileName));
        var escapedDatabaseName = databaseName.Replace("]", "]]", StringComparison.Ordinal);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // بعض إصدارات SQL Server Express لا تسمح بإنشاء نسخة مضغوطة.
        // نجرب الضغط أولًا، ثم نعيد المحاولة دون ضغط إذا رفضه المحرك.
        try
        {
            await ExecuteBackupAsync(connection, escapedDatabaseName, filePath, useCompression: true, cancellationToken: cancellationToken);
        }
        catch (SqlException ex) when (IsCompressionUnsupported(ex))
        {
            logger.LogWarning(ex, "ضغط النسخة الاحتياطية غير مدعوم؛ ستتم إعادة المحاولة دون ضغط.");
            if (File.Exists(filePath))
                File.Delete(filePath);

            await ExecuteBackupAsync(connection, escapedDatabaseName, filePath, useCompression: false, cancellationToken: cancellationToken);
        }

        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length == 0)
            throw new InvalidOperationException("أنشأ SQL Server أمر النسخ الاحتياطي دون ملف صالح.");

        logger.LogInformation("تم إنشاء نسخة احتياطية لقاعدة البيانات: {BackupFile}", filePath);
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.UserId,
            Action = "DatabaseBackup",
            EntityName = "Database",
            EntityId = databaseName,
            NewValues = $"File={fileName};Size={info.Length}",
            IpAddress = actor.IpAddress,
            ActionDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        CleanupOldBackups(backupDirectory, safeDatabaseName);

        return new DatabaseBackupResult(filePath, info.Length, DateTimeOffset.Now);
    }

    private static async Task ExecuteBackupAsync(
        SqlConnection connection,
        string escapedDatabaseName,
        string filePath,
        bool useCompression,
        CancellationToken cancellationToken)
    {
        var compressionOption = useCompression ? ", COMPRESSION" : string.Empty;
        var sql = $"""
                  DECLARE @backupPath nvarchar(4000) = @path;
                  BACKUP DATABASE [{escapedDatabaseName}]
                  TO DISK = @backupPath
                  WITH COPY_ONLY, INIT{compressionOption}, CHECKSUM, STATS = 10;
                  RESTORE VERIFYONLY FROM DISK = @backupPath WITH CHECKSUM;
                  """;

        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = 600
        };
        command.Parameters.Add(new SqlParameter("@path", System.Data.SqlDbType.NVarChar, 4000)
        {
            Value = filePath
        });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsCompressionUnsupported(SqlException exception)
    {
        // 909/1844/3201 قد تختلف حسب الإصدار واللغة؛ النص الاحتياطي يغطي الرسائل المترجمة جزئيًا.
        return exception.Errors.Cast<SqlError>().Any(error => error.Number is 909 or 1844) ||
               exception.Message.Contains("COMPRESSION", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("compression", StringComparison.OrdinalIgnoreCase);
    }

    private void CleanupOldBackups(string directory, string databaseName)
    {
        var keep = Math.Clamp(configuration.GetValue("DatabaseBackup:KeepLatest", 30), 3, 365);
        try
        {
            foreach (var file in new DirectoryInfo(directory)
                         .EnumerateFiles($"{databaseName}_*.bak")
                         .OrderByDescending(x => x.CreationTimeUtc)
                         .Skip(keep))
            {
                file.Delete();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "تعذر تنظيف النسخ الاحتياطية القديمة.");
        }
    }
}
