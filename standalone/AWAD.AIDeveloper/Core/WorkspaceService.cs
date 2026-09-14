using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace AWAD.AIDeveloper.Core;

public sealed class WorkspaceService
{
    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".idea", ".vs", "node_modules", "bin", "obj", "dist", "build", ".next", ".nuxt", "coverage", "packages"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".sln", ".razor", ".xaml", ".xml", ".json", ".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".scss",
        ".py", ".java", ".kt", ".kts", ".go", ".rs", ".cpp", ".c", ".h", ".hpp", ".md", ".txt", ".yml", ".yaml", ".toml",
        ".ini", ".properties", ".gradle", ".ps1", ".sh", ".cmd", ".bat", ".sql", ".php", ".vue", ".dart"
    };

    private static readonly string[] SecretFragments =
    {
        ".env", "secret", "credential", "password", "apikey", "api_key", "privatekey", "private_key", ".pfx", ".p12", ".jks", "keystore", "id_rsa"
    };

    private static readonly HashSet<string> AllowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet", "dotnet.exe", "npm", "npm.cmd", "npx", "npx.cmd", "node", "node.exe", "git", "git.exe",
        "python", "python.exe", "python3", "py", "py.exe", "pytest", "pytest.exe", "cargo", "cargo.exe", "go", "go.exe",
        "mvn", "mvn.cmd", "mvnw", "mvnw.cmd", "gradle", "gradle.bat", "gradlew", "gradlew.bat", "java", "java.exe"
    };

    public string OpenProject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("مسار المشروع مطلوب.");
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("مجلد المشروع غير موجود.");
        return full;
    }

    public object[] GetTree(string root, int maxFiles) => EnumerateProjectFiles(root).Take(maxFiles).Select(file =>
    {
        var info = new FileInfo(file);
        return (object)new { path = Path.GetRelativePath(root, file).Replace('\\', '/'), size = info.Length, modified = info.LastWriteTimeUtc };
    }).ToArray();

    public string BuildProjectContext(string root, string task, int charBudget = 110_000)
    {
        var files = EnumerateProjectFiles(root).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("PROJECT TREE:");
        foreach (var file in files.Take(500)) sb.AppendLine(Path.GetRelativePath(root, file).Replace('\\', '/'));
        sb.AppendLine();

        var tokens = task.Split(new[] { ' ', '\t', '\r', '\n', '/', '\\', '.', ',', ':', ';', '،' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();

        var candidates = files.Where(IsSafeTextFile).Select(f => new
        {
            File = f,
            Rel = Path.GetRelativePath(root, f).Replace('\\', '/'),
            Score = ScoreFile(f, tokens),
            Size = new FileInfo(f).Length
        }).Where(x => x.Size <= 150_000).OrderByDescending(x => x.Score).ThenBy(x => x.Size).Take(18);

        foreach (var c in candidates)
        {
            if (sb.Length >= charBudget) break;
            try
            {
                var content = File.ReadAllText(c.File);
                var remaining = charBudget - sb.Length;
                if (remaining < 1500) break;
                if (content.Length > remaining - 300) content = content[..Math.Max(0, remaining - 300)];
                sb.AppendLine($"--- FILE: {c.Rel} ---");
                sb.AppendLine(content);
                sb.AppendLine($"--- END FILE: {c.Rel} ---");
            }
            catch { }
        }
        return sb.ToString();
    }

    public string CreateCheckpoint(string root, string backupRoot)
    {
        Directory.CreateDirectory(backupRoot);
        var zipPath = Path.Combine(backupRoot, $"{Sanitize(Path.GetFileName(root))}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in EnumerateProjectFiles(root))
        {
            if (new FileInfo(file).Length > 25 * 1024 * 1024) continue;
            archive.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Fastest);
        }
        return zipPath;
    }

    public string CreateStagingCopy(string root, string stagingRoot)
    {
        var target = Path.Combine(stagingRoot, $"{Sanitize(Path.GetFileName(root))}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(target);
        foreach (var file in EnumerateProjectFiles(root))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(root, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
        return target;
    }

    public async Task<ApplyResult> ApplyPlanAsync(string root, AgentPlan plan, string backupRoot, CancellationToken ct)
    {
        var checkpoint = CreateCheckpoint(root, backupRoot);
        var logs = new List<ActionLog>();
        var success = true;
        foreach (var action in plan.Actions.Take(24))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                switch (action.Type)
                {
                    case "write_file":
                        {
                            var path = ResolveWritablePath(root, action.Path);
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            await File.WriteAllTextAsync(path, action.Content ?? string.Empty, new UTF8Encoding(false), ct);
                            logs.Add(new(action.Type, action.Path ?? "", true, "تمت كتابة الملف."));
                            break;
                        }
                    case "replace_text":
                        {
                            var path = ResolveWritablePath(root, action.Path);
                            if (!File.Exists(path)) throw new FileNotFoundException("الملف المطلوب للتعديل غير موجود.", action.Path);
                            var text = await File.ReadAllTextAsync(path, ct);
                            if (string.IsNullOrEmpty(action.Old)) throw new InvalidOperationException("old مطلوب في replace_text.");
                            if (!text.Contains(action.Old, StringComparison.Ordinal)) throw new InvalidOperationException("النص القديم غير موجود حرفياً في الملف.");
                            await File.WriteAllTextAsync(path, text.Replace(action.Old, action.New ?? string.Empty, StringComparison.Ordinal), new UTF8Encoding(false), ct);
                            logs.Add(new(action.Type, action.Path ?? "", true, "تم استبدال النص."));
                            break;
                        }
                    case "delete_file":
                        {
                            var path = ResolveWritablePath(root, action.Path);
                            if (File.Exists(path)) File.Delete(path);
                            logs.Add(new(action.Type, action.Path ?? "", true, "تم حذف الملف."));
                            break;
                        }
                    case "run_command":
                        {
                            var result = await RunCommandAsync(root, action.Command ?? string.Empty, ct);
                            logs.Add(new(action.Type, action.Command ?? "", result.ExitCode == 0, result.Output));
                            if (result.ExitCode != 0) success = false;
                            break;
                        }
                    case "note":
                        logs.Add(new(action.Type, action.Path ?? "", true, action.Content ?? action.Reason ?? ""));
                        break;
                    default:
                        throw new InvalidOperationException($"نوع الإجراء غير مسموح: {action.Type}");
                }
            }
            catch (Exception ex)
            {
                success = false;
                logs.Add(new(action.Type, action.Path ?? action.Command ?? "", false, ex.Message));
                if (action.Type == "run_command") break;
            }
        }
        return new ApplyResult(success, checkpoint, logs, plan.Status, plan.Summary);
    }

    private async Task<CommandResult> RunCommandAsync(string root, string command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("الأمر فارغ.");
        if (command.Contains('\n') || command.Contains('\r')) throw new InvalidOperationException("الأوامر متعددة الأسطر غير مسموحة.");
        var (exe, args) = SplitExecutable(command.Trim());
        var exeName = Path.GetFileName(exe);
        if (!AllowedExecutables.Contains(exeName)) throw new InvalidOperationException($"الأمر غير موجود في قائمة التنفيذ الآمن: {exeName}");

        var fileName = exe;
        var arguments = args;
        if (OperatingSystem.IsWindows() && (exeName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exeName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            if (args.IndexOfAny(['&', '|', '>', '<']) >= 0) throw new InvalidOperationException("رموز ربط أو إعادة توجيه shell غير مسموحة.");
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
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException("تجاوز الأمر مهلة التنفيذ (4 دقائق)." );
        }
        var output = (await stdoutTask) + Environment.NewLine + (await stderrTask);
        if (output.Length > 30_000) output = output[^30_000..];
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

    private string ResolveWritablePath(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) throw new InvalidOperationException("مسار الملف مطلوب.");
        if (Path.IsPathRooted(relative)) throw new InvalidOperationException("المسارات المطلقة غير مسموحة.");
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("محاولة كتابة خارج مجلد المشروع مرفوضة.");
        var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
        if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) || rel.Equals(".git", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("التعديل المباشر داخل .git مرفوض.");
        if (IsSecretPath(rel)) throw new InvalidOperationException("التعديل على ملفات الأسرار/المفاتيح مرفوض تلقائياً.");
        return full;
    }

    private IEnumerable<string> EnumerateProjectFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> dirs; IEnumerable<string> files;
            try { dirs = Directory.EnumerateDirectories(dir); files = Directory.EnumerateFiles(dir); }
            catch { continue; }
            foreach (var d in dirs) if (!SkipDirectories.Contains(Path.GetFileName(d))) stack.Push(d);
            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!IsSecretPath(rel)) yield return file;
            }
        }
    }

    private static bool IsSafeTextFile(string file) => !IsSecretPath(file) && (TextExtensions.Contains(Path.GetExtension(file)) || string.IsNullOrEmpty(Path.GetExtension(file)));
    private static bool IsSecretPath(string path)
    {
        var lower = path.Replace('\\', '/').ToLowerInvariant();
        return SecretFragments.Any(f => lower.Contains(f, StringComparison.OrdinalIgnoreCase));
    }
    private static int ScoreFile(string file, string[] tokens)
    {
        var rel = file.Replace('\\', '/'); var name = Path.GetFileName(file); var score = 0;
        foreach (var t in tokens) { if (name.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 8; else if (rel.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 3; }
        if (name.Equals("README.md", StringComparison.OrdinalIgnoreCase)) score += 6;
        if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || name is "package.json" or "pyproject.toml") score += 5;
        return score;
    }
    private static string Sanitize(string value)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) value = value.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(value) ? "project" : value;
    }
}
