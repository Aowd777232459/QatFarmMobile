using System.Diagnostics;
using System.Text;

namespace AWAD.AIDeveloper.Core;

public sealed class DeveloperToolsService
{
    private static readonly HashSet<string> AllowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", "dotnet.exe", "npm", "npm.cmd", "npx", "npx.cmd", "node", "node.exe", "git", "git.exe",
        "python", "python.exe", "python3", "py", "py.exe", "pytest", "pytest.exe", "cargo", "cargo.exe", "go", "go.exe",
        "mvn", "mvn.cmd", "mvnw", "mvnw.cmd", "gradle", "gradle.bat", "gradlew", "gradlew.bat", "java", "java.exe"
    };

    private static readonly string[] SecretFragments =
    {
        ".env", "secret", "credential", "password", "apikey", "api_key", "privatekey", "private_key", ".pfx", ".p12", ".jks", "keystore", "id_rsa"
    };

    private readonly WorkspaceService _workspace;
    private readonly AppState _state;

    public DeveloperToolsService(WorkspaceService workspace, AppState state)
    {
        _workspace = workspace;
        _state = state;
    }

    public FileDocument ReadFile(string root, string relative)
    {
        var full = ResolveSafePath(root, relative, write: false);
        if (!File.Exists(full)) throw new FileNotFoundException("الملف غير موجود.", relative);
        var info = new FileInfo(full);
        if (info.Length > 1024 * 1024) throw new InvalidOperationException("حجم الملف أكبر من 1MB ولا يُفتح في المحرر المدمج.");
        var content = File.ReadAllText(full);
        if (content.IndexOf('\0') >= 0) throw new InvalidOperationException("الملف ثنائي وليس ملفًا نصيًا.");
        return new FileDocument(relative.Replace('\\', '/'), content, info.Length, info.LastWriteTimeUtc);
    }

    public FileSaveResult SaveFile(string root, string relative, string? content)
    {
        var full = ResolveSafePath(root, relative, write: true);
        var checkpoint = _workspace.CreateCheckpoint(root, _state.BackupsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content ?? string.Empty, new UTF8Encoding(false));
        return new FileSaveResult(true, checkpoint, relative.Replace('\\', '/'));
    }

    public IReadOnlyList<PlanPreviewItem> PreviewPlan(string root, AgentPlan plan)
    {
        var result = new List<PlanPreviewItem>();
        foreach (var action in plan.Actions.Where(x => x.Type is "write_file" or "replace_text" or "delete_file").Take(24))
        {
            if (string.IsNullOrWhiteSpace(action.Path)) continue;
            var full = ResolveSafePath(root, action.Path, write: false, allowMissing: true);
            var before = File.Exists(full) ? SafeRead(full) : string.Empty;
            var after = before;
            try
            {
                if (action.Type == "write_file") after = action.Content ?? string.Empty;
                else if (action.Type == "delete_file") after = string.Empty;
                else if (action.Type == "replace_text" && !string.IsNullOrEmpty(action.Old))
                    after = before.Replace(action.Old, action.New ?? string.Empty, StringComparison.Ordinal);
            }
            catch { }
            result.Add(new PlanPreviewItem(action.Path.Replace('\\', '/'), action.Type, BuildCompactDiff(before, after)));
        }
        return result;
    }

    public async Task<CommandResult> RunTerminalAsync(string root, string command, CancellationToken ct)
        => await RunSafeCommandAsync(root, command, TimeSpan.FromMinutes(5), ct);

    public async Task<GitSnapshot> GetGitSnapshotAsync(string root, CancellationToken ct)
    {
        if (!Directory.Exists(Path.Combine(root, ".git")))
            return new GitSnapshot(false, "هذا المجلد ليس مستودع Git.", string.Empty, string.Empty, string.Empty);

        var status = await RunSafeCommandAsync(root, "git status --short --branch", TimeSpan.FromSeconds(30), ct);
        var diff = await RunSafeCommandAsync(root, "git diff --stat", TimeSpan.FromSeconds(30), ct);
        var log = await RunSafeCommandAsync(root, "git log -5 --oneline --decorate", TimeSpan.FromSeconds(30), ct);
        return new GitSnapshot(true, status.Output, diff.Output, log.Output, DetectBranch(status.Output));
    }

    public async Task<CommandResult> CreateGitCheckpointAsync(string root, string? message, CancellationToken ct)
    {
        if (!Directory.Exists(Path.Combine(root, ".git"))) throw new InvalidOperationException("المشروع ليس مستودع Git.");
        var safeMessage = string.IsNullOrWhiteSpace(message) ? $"AWAD AI checkpoint {DateTime.Now:yyyy-MM-dd HH:mm}" : message.Trim();
        safeMessage = safeMessage.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
        if (safeMessage.Length > 120) safeMessage = safeMessage[..120];

        var add = await RunSafeCommandAsync(root, "git add -A", TimeSpan.FromMinutes(2), ct);
        if (add.ExitCode != 0) return add;
        var staged = await RunSafeCommandAsync(root, "git diff --cached --quiet", TimeSpan.FromSeconds(30), ct);
        if (staged.ExitCode == 0) return new CommandResult(0, "لا توجد تغييرات جديدة لإنشاء checkpoint.");
        return await RunSafeCommandAsync(root, $"git commit -m \"{safeMessage}\"", TimeSpan.FromMinutes(2), ct);
    }

    private static string DetectBranch(string status)
    {
        var first = status.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (!first.StartsWith("## ", StringComparison.Ordinal)) return string.Empty;
        var name = first[3..];
        var dots = name.IndexOf("...", StringComparison.Ordinal);
        return dots >= 0 ? name[..dots] : name;
    }

    private static string BuildCompactDiff(string before, string after)
    {
        const int maxLines = 220;
        var oldLines = Normalize(before).Split('\n');
        var newLines = Normalize(after).Split('\n');
        var sb = new StringBuilder();
        var max = Math.Max(oldLines.Length, newLines.Length);
        var emitted = 0;
        for (var i = 0; i < max && emitted < maxLines; i++)
        {
            var oldLine = i < oldLines.Length ? oldLines[i] : null;
            var newLine = i < newLines.Length ? newLines[i] : null;
            if (string.Equals(oldLine, newLine, StringComparison.Ordinal))
            {
                if (i < 4 || i >= max - 4)
                {
                    sb.Append("  ").AppendLine(TrimLine(oldLine));
                    emitted++;
                }
                continue;
            }
            if (oldLine is not null) { sb.Append("- ").AppendLine(TrimLine(oldLine)); emitted++; }
            if (newLine is not null && emitted < maxLines) { sb.Append("+ ").AppendLine(TrimLine(newLine)); emitted++; }
        }
        if (max > maxLines) sb.AppendLine("... تم اختصار المعاينة ...");
        return sb.Length == 0 ? "لا يوجد فرق نصي ظاهر." : sb.ToString().TrimEnd();
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    private static string TrimLine(string? line)
    {
        line ??= string.Empty;
        return line.Length <= 500 ? line : line[..500] + " …";
    }

    private static string SafeRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 1024 * 1024) return "[ملف أكبر من 1MB - تم تجاوز المعاينة]";
            var text = File.ReadAllText(path);
            return text.IndexOf('\0') >= 0 ? "[ملف ثنائي]" : text;
        }
        catch (Exception ex) { return $"[تعذر القراءة: {ex.Message}]"; }
    }

    private static async Task<CommandResult> RunSafeCommandAsync(string root, string command, TimeSpan timeoutValue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("الأمر فارغ.");
        if (command.Contains('\n') || command.Contains('\r')) throw new InvalidOperationException("الأوامر متعددة الأسطر غير مسموحة.");
        if (command.IndexOfAny(['&', '|', '>', '<', '`']) >= 0) throw new InvalidOperationException("رموز ربط shell وإعادة التوجيه غير مسموحة.");

        var (exe, args) = SplitExecutable(command.Trim());
        var exeName = Path.GetFileName(exe);
        if (!AllowedExecutables.Contains(exeName)) throw new InvalidOperationException($"الأمر غير مسموح: {exeName}");

        var fileName = exe;
        var arguments = args;
        if (OperatingSystem.IsWindows() && (exeName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exeName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            fileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            arguments = $"/d /s /c \"\\\"{exe}\\\" {args}\"";
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutValue);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"تجاوز الأمر مهلة التنفيذ ({timeoutValue.TotalMinutes:0.#} دقيقة)." );
        }
        var output = (await stdout) + Environment.NewLine + (await stderr);
        if (output.Length > 60_000) output = output[^60_000..];
        return new CommandResult(process.ExitCode, output.Trim());
    }

    private static (string Exe, string Args) SplitExecutable(string command)
    {
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end < 1) throw new InvalidOperationException("صيغة الأمر غير صحيحة.");
            return (command[1..end], command[(end + 1)..].Trim());
        }
        var space = command.IndexOf(' ');
        return space < 0 ? (command, string.Empty) : (command[..space], command[(space + 1)..].Trim());
    }

    private static string ResolveSafePath(string root, string? relative, bool write, bool allowMissing = false)
    {
        if (string.IsNullOrWhiteSpace(relative)) throw new InvalidOperationException("مسار الملف مطلوب.");
        if (Path.IsPathRooted(relative)) throw new InvalidOperationException("المسارات المطلقة غير مسموحة.");
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("المسار خارج مجلد المشروع.");
        var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
        if (rel.Equals(".git", StringComparison.OrdinalIgnoreCase) || rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فتح أو تعديل ملفات .git مباشرة غير مسموح.");
        if (IsSecretPath(rel)) throw new InvalidOperationException("ملفات الأسرار والمفاتيح محمية.");
        if (!allowMissing && !File.Exists(full) && !write) throw new FileNotFoundException("الملف غير موجود.", relative);
        return full;
    }

    private static bool IsSecretPath(string path)
    {
        var lower = path.Replace('\\', '/').ToLowerInvariant();
        return SecretFragments.Any(x => lower.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
}
