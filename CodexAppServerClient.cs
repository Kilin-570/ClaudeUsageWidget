using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace ClaudeUsageWidget;

public sealed class CodexUnavailableException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// <summary>
/// Minimal JSON-RPC client for the stable Codex app-server account API. The widget never
/// reads or copies Codex auth files: the official Codex process owns login, refresh, and
/// upstream requests while this client only exchanges JSONL over redirected stdio.
/// </summary>
public sealed class CodexAppServerClient(Func<string?> configuredPath) : IDisposable
{
    readonly Func<string?> _configuredPath = configuredPath;
    readonly SemaphoreSlim _startGate = new(1, 1);
    readonly SemaphoreSlim _writeGate = new(1, 1);
    readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    readonly CancellationTokenSource _lifetime = new();

    Process? _process;
    StreamWriter? _writer;
    Task? _stdoutTask;
    Task? _stderrTask;
    long _nextId;
    bool _disposed;

    public async Task<JsonElement> RequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken);
        return await RequestCoreAsync(method, parameters, cancellationToken);
    }

    async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && _writer is not null) return;

        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false } && _writer is not null) return;
            StopProcess();

            var executable = CodexLocator.Resolve(_configuredPath());
            var psi = CodexLocator.CreateAppServerStartInfo(executable);
            try
            {
                _process = Process.Start(psi)
                    ?? throw new CodexUnavailableException(L10n.T("err_codex_start"));
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
            {
                throw new CodexUnavailableException(L10n.F("err_codex_missing", executable), ex);
            }

            _writer = _process.StandardInput;
            _writer.AutoFlush = true;
            _stdoutTask = ReadStdoutAsync(_process, _lifetime.Token);
            _stderrTask = DrainStderrAsync(_process, _lifetime.Token);

            using var initTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            initTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            await RequestCoreAsync("initialize", new
            {
                clientInfo = new
                {
                    name = "ai_usage_widget",
                    title = "Claude + ChatGPT Usage Widget",
                    version = UpdateService.Current.ToString(),
                },
                capabilities = new
                {
                    optOutNotificationMethods = new[]
                    {
                        "thread/started",
                        "item/agentMessage/delta",
                    },
                },
            }, initTimeout.Token);
            await SendNotificationAsync("initialized", cancellationToken);
        }
        catch
        {
            StopProcess();
            throw;
        }
        finally
        {
            _startGate.Release();
        }
    }

    async Task<JsonElement> RequestCoreAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
            throw new InvalidOperationException("Duplicate Codex app-server request id.");

        try
        {
            var message = new Dictionary<string, object?>
            {
                ["method"] = method,
                ["id"] = id,
            };
            if (parameters is not null) message["params"] = parameters;

            await WriteLineAsync(JsonSerializer.Serialize(message), cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            return await completion.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    async Task SendNotificationAsync(string method, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["method"] = method,
        });
        await WriteLineAsync(message, cancellationToken);
    }

    async Task WriteLineAsync(string json, CancellationToken cancellationToken)
    {
        var writer = _writer ?? throw new CodexUnavailableException(L10n.T("err_codex_stopped"));
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteLineAsync(json).WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            throw new CodexUnavailableException(L10n.T("err_codex_stopped"), ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    async Task ReadStdoutAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null) break;
                if (line.Length == 0 || line[0] != '{') continue;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("id", out var idElement) ||
                        idElement.ValueKind != JsonValueKind.Number ||
                        !idElement.TryGetInt64(out var id) ||
                        !_pending.TryGetValue(id, out var completion))
                        continue; // notification or response for a request we no longer await

                    if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                    {
                        var code = error.TryGetProperty("code", out var codeValue) ? codeValue.ToString() : "unknown";
                        var message = error.TryGetProperty("message", out var messageValue)
                            ? messageValue.GetString() ?? "Codex app-server error"
                            : "Codex app-server error";
                        completion.TrySetException(new InvalidOperationException($"Codex app-server {code}: {message}"));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        completion.TrySetResult(result.Clone());
                    }
                    else
                    {
                        completion.TrySetResult(default);
                    }
                }
                catch (JsonException)
                {
                    // Ignore non-protocol output. Never write full app-server lines to our log;
                    // future versions could include account metadata in diagnostic output.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Error("Codex app-server output loop stopped", ex);
        }
        finally
        {
            FailPending(new CodexUnavailableException(L10n.T("err_codex_stopped")));
        }
    }

    static async Task DrainStderrAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null) break;
                // Deliberately discard stderr. It may contain local paths or account metadata.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { }
    }

    void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
            completion.TrySetException(exception);
    }

    void StopProcess()
    {
        _writer?.Dispose();
        _writer = null;
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch { }
            _process.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        StopProcess();
        FailPending(new ObjectDisposedException(nameof(CodexAppServerClient)));
        _lifetime.Dispose();
        _startGate.Dispose();
        _writeGate.Dispose();
    }
}

static class CodexLocator
{
    public static string Resolve(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath.Trim()));
            if (File.Exists(full)) return full;
            throw new CodexUnavailableException(L10n.F("err_codex_path_invalid", full));
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
            return Path.GetFullPath(fromEnvironment);

        // The unified ChatGPT desktop app keeps its runnable Codex CLI in a
        // user-accessible, versioned directory. Prefer it over PATH because processes
        // launched by ChatGPT can inherit a protected WindowsApps path that exists but
        // cannot be started by an unpackaged widget.
        var desktopBundled = FindChatGptDesktopCodex(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (desktopBundled is not null) return desktopBundled;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string dir;
            try { dir = directory.Trim().Trim('"'); }
            catch { continue; }
            foreach (var name in new[] { "codex.exe", "codex.cmd", "codex.bat" })
            {
                try
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }

        // Let Windows resolve an App Execution Alias if one exists.
        return "codex.exe";
    }

    internal static string? FindChatGptDesktopCodex(string localApplicationData)
    {
        if (string.IsNullOrWhiteSpace(localApplicationData)) return null;

        string binRoot;
        try
        {
            binRoot = Path.Combine(localApplicationData, "OpenAI", "Codex", "bin");
        }
        catch
        {
            return null;
        }

        var candidates = new List<(string Path, DateTime LastWriteUtc)>();

        void AddCandidate(string candidate)
        {
            try
            {
                if (!File.Exists(candidate)) return;
                candidates.Add((Path.GetFullPath(candidate), File.GetLastWriteTimeUtc(candidate)));
            }
            catch
            {
                // An app update may be replacing a candidate while we enumerate it.
            }
        }

        // Support both a future stable layout and today's one-level version/hash layout.
        AddCandidate(Path.Combine(binRoot, "codex.exe"));
        try
        {
            foreach (var versionDirectory in Directory.EnumerateDirectories(binRoot))
                AddCandidate(Path.Combine(versionDirectory, "codex.exe"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return candidates
                .OrderByDescending(candidate => candidate.LastWriteUtc)
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.Path)
                .FirstOrDefault();
        }

        return candidates
            .OrderByDescending(candidate => candidate.LastWriteUtc)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    public static ProcessStartInfo CreateAppServerStartInfo(string executable)
    {
        ProcessStartInfo startInfo;
        var extension = Path.GetExtension(executable);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var command = $"\"{executable.Replace("\"", "") }\" app-server --stdio";
            startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            startInfo = new ProcessStartInfo { FileName = executable };
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");
        }

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
        startInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
        return startInfo;
    }
}
