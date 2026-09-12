using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Maui.Storage;
using QatFarm.Mobile.Data;
using QatFarm.Mobile.Models;

#if WINDOWS
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
#endif

namespace QatFarm.Mobile.Services;

/// <summary>
/// خادم مزامنة محلي يعمل داخل نسخة Windows.
/// الجوال والكمبيوتر يتبادلان نفس سجلات SQLite في الاتجاهين مع قاعدة "الأحدث يفوز".
/// </summary>
public sealed class DesktopSyncServer : IAsyncDisposable
{
    private readonly LocalSyncService _sync;
    private readonly MobileDb _db;
    private readonly AwadStorageService _storage;
    private readonly SemaphoreSlim _mergeGate = new(1, 1);
#if WINDOWS
    private WebApplication? _app;
#endif

    public int Port { get; } = 5276;
    public string PairingKey { get; }
    public string LocalIpAddress => ResolveLocalIPv4();
    public string ServerUrl => $"http://{LocalIpAddress}:{Port}";
    public bool IsRunning { get; private set; }
    public DateTimeOffset? LastSyncAt { get; private set; }
    public string LastMessage { get; private set; } = "خادم المزامنة جاهز للبدء.";
    public event Action? Changed;

    public DesktopSyncServer(LocalSyncService sync, MobileDb db, AwadStorageService storage)
    {
        _sync = sync;
        _db = db;
        _storage = storage;
        var saved = Preferences.Default.Get("DesktopSync.PairingKey", string.Empty);
        if (string.IsNullOrWhiteSpace(saved))
        {
            saved = $"AWAD-{RandomNumberGenerator.GetInt32(100000, 999999)}";
            Preferences.Default.Set("DesktopSync.PairingKey", saved);
        }
        PairingKey = saved;
        WriteConnectionCard();
    }

    public async Task StartAsync()
    {
#if WINDOWS
        if (_app is not null) return;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");
            var app = builder.Build();

            app.MapGet("/api/local-sync/ping", () => Results.Json(new
            {
                product = "AWAD SOFT QatFarm",
                version = "2.3.0",
                device = Environment.MachineName,
                serverTime = DateTimeOffset.Now
            }));

            app.MapPost("/api/local-sync/sync", async (HttpRequest request, CancellationToken token) =>
            {
                if (!request.Headers.TryGetValue("X-AWAD-SYNC-KEY", out var supplied) ||
                    !CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.UTF8.GetBytes(supplied.ToString()),
                        System.Text.Encoding.UTF8.GetBytes(PairingKey)))
                {
                    return Results.Unauthorized();
                }

                LocalSyncBatch? incoming;
                try
                {
                    incoming = await request.ReadFromJsonAsync<LocalSyncBatch>(cancellationToken: token);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest("دفعة المزامنة غير صالحة: " + ex.Message);
                }
                if (incoming is null) return Results.BadRequest("دفعة المزامنة فارغة.");

                await _mergeGate.WaitAsync(token);
                try
                {
                    var before = await BuildLocalBatchAsync();
                    var local = before.Records.ToDictionary(x => RecordId(x), StringComparer.Ordinal);
                    var newerFromPhone = incoming.Records
                        .Where(x => !local.TryGetValue(RecordId(x), out var existing) || x.UpdatedAtUtc > existing.UpdatedAtUtc)
                        .ToList();

                    if (newerFromPhone.Count > 0)
                        await ApplyPeerRecordsAsync(newerFromPhone);

                    var after = await BuildLocalBatchAsync();
                    LastSyncAt = DateTimeOffset.Now;
                    LastMessage = $"تمت المزامنة بنجاح — {LastSyncAt:yyyy/MM/dd HH:mm}";
                    WriteConnectionCard();
                    await _storage.CreateAutomaticBackupAsync(_db, "sync");
                    Changed?.Invoke();

                    return Results.Json(new LocalSyncResult
                    {
                        Success = true,
                        Message = LastMessage,
                        ServerTime = DateTimeOffset.UtcNow,
                        Received = incoming.Records.Count,
                        Records = after.Records
                    });
                }
                catch (Exception ex)
                {
                    LastMessage = "فشل دمج المزامنة: " + ex.Message;
                    Changed?.Invoke();
                    return Results.Problem(LastMessage);
                }
                finally
                {
                    _mergeGate.Release();
                }
            });

            await app.StartAsync();
            _app = app;
            IsRunning = true;
            LastMessage = $"المزامنة تعمل على {ServerUrl}";
            WriteConnectionCard();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            IsRunning = false;
            LastMessage = "تعذر تشغيل خادم المزامنة: " + ex.Message;
            WriteConnectionCard();
            Changed?.Invoke();
        }
#else
        await Task.CompletedTask;
#endif
    }

    private async Task<LocalSyncBatch> BuildLocalBatchAsync()
    {
        var method = typeof(LocalSyncService).GetMethod("BuildBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(nameof(LocalSyncService), "BuildBatchAsync");
        var task = (Task<LocalSyncBatch>?)method.Invoke(_sync, null)
                   ?? throw new InvalidOperationException("تعذر قراءة بيانات المزامنة المحلية.");
        return await task;
    }

    private async Task ApplyPeerRecordsAsync(List<LocalSyncRecord> records)
    {
        var method = typeof(LocalSyncService).GetMethod("ApplyServerRecordsAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(nameof(LocalSyncService), "ApplyServerRecordsAsync");
        var task = (Task?)method.Invoke(_sync, new object[] { records })
                   ?? throw new InvalidOperationException("تعذر تطبيق بيانات الجوال على الكمبيوتر.");
        await task;
    }

    private void WriteConnectionCard()
    {
        try
        {
            _storage.EnsureStructure();
            var text =
                "AWAD SOFT - ربط الجوال بالكمبيوتر\r\n" +
                "==================================\r\n" +
                $"عنوان الكمبيوتر: {ServerUrl}\r\n" +
                $"رمز الربط: {PairingKey}\r\n" +
                $"الحالة: {LastMessage}\r\n" +
                "\r\nأدخل عنوان الكمبيوتر ورمز الربط في: الإعدادات > مزامنة Wi-Fi داخل تطبيق الجوال.\r\n" +
                "يجب أن يكون الجوال والكمبيوتر على نفس الشبكة.\r\n";
            File.WriteAllText(Path.Combine(_storage.SyncPath, "بيانات-الربط.txt"), text, System.Text.Encoding.UTF8);
        }
        catch { }
    }

    private static string RecordId(LocalSyncRecord record) => record.Entity + ":" + record.Key;

    private static string ResolveLocalIPv4()
    {
        try
        {
            var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
            var preferred = addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x));
            return preferred?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    public async ValueTask DisposeAsync()
    {
#if WINDOWS
        if (_app is not null)
        {
            try { await _app.StopAsync(); } catch { }
            await _app.DisposeAsync();
            _app = null;
        }
#endif
        _mergeGate.Dispose();
    }
}
