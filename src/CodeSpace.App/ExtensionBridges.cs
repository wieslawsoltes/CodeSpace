using System.Text.Json;
using CodeSpace.Extensions;
using Microsoft.UI.Xaml;

namespace CodeSpace.App;

#if __WASM__
internal sealed class BrowserExtensionBridge : IExtensionBridge
{
    private readonly DispatcherTimer _pump = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private bool _started;
    public bool IsAvailable => true;
    public event EventHandler<string>? MessageReceived;
    public BrowserExtensionBridge()
    {
        _pump.Tick += (_, _) =>
        {
            if (!_started) return;
            try
            {
                var json = Uno.Foundation.WebAssemblyRuntime.InvokeJS("JSON.stringify(globalThis.CodeSpaceHost?.drain() ?? [])");
                using var document = JsonDocument.Parse(json);
                foreach (var message in document.RootElement.EnumerateArray()) MessageReceived?.Invoke(this, message.GetRawText());
            }
            catch (Exception exception) { MessageReceived?.Invoke(this, JsonSerializer.Serialize(new { type = "error", message = exception.Message })); }
        };
    }
    public Task StartAsync(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested(); if (_started) return Task.CompletedTask;
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("""
            (() => {
                if (globalThis.CodeSpaceHost) return 'ready';
                const queue = [];
                const worker = new Worker(new URL('extension-host/worker.mjs', document.baseURI), { type: 'module', name: 'CodeSpace extension host' });
                worker.onmessage = e => { if (queue.length < 4096) queue.push(e.data); };
                worker.onerror = e => queue.push({ type: 'error', message: 'Extension worker: ' + e.message });
                globalThis.CodeSpaceHost = {
                    send: message => worker.postMessage(message),
                    drain: () => queue.splice(0, 200),
                    stop: () => { worker.terminate(); delete globalThis.CodeSpaceHost; }
                };
                return 'ready';
            })()
            """);
        _started = true; _pump.Start(); return Task.CompletedTask;
    }
    public Task SendAsync(string json, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested(); if (!_started) throw new InvalidOperationException("Extension host has not started.");
        // Serialize the entire JSON string rather than interpolate executable extension source into the UI realm.
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.CodeSpaceHost.send(JSON.parse(" + JsonSerializer.Serialize(json) + ")); 'sent'");
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync()
    {
        _pump.Stop(); if (_started) Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.CodeSpaceHost?.stop(); 'stopped'"); _started = false; return ValueTask.CompletedTask;
    }
}
#else
internal sealed class DesktopExtensionBridge : IExtensionBridge
{
    private System.Diagnostics.Process? _process;
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly string _entry = Path.Combine(AppContext.BaseDirectory, "extension-host", "node-host.mjs");
    private CancellationTokenSource? _lifetime;
    public bool IsAvailable => File.Exists(_entry);
    public event EventHandler<string>? MessageReceived;
    public Task StartAsync(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested(); if (_process is { HasExited: false }) return Task.CompletedTask;
        if (!File.Exists(_entry)) throw new FileNotFoundException("The packaged Node extension host was not found.", _entry);
        var info = new System.Diagnostics.ProcessStartInfo("node") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        info.ArgumentList.Add(_entry);
        try { _process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Could not start Node.js."); }
        catch (Exception exception) { throw new InvalidOperationException("Install Node.js 22 or newer on PATH to use desktop extensions. " + exception.Message, exception); }
        _lifetime = new CancellationTokenSource(); _ = ReadAsync(_process.StandardOutput, false, _lifetime.Token); _ = ReadAsync(_process.StandardError, true, _lifetime.Token); return Task.CompletedTask;
    }
    private async Task ReadAsync(StreamReader reader, bool error, CancellationToken cancellation)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
            {
                if (line.Length > 16 * 1024 * 1024) { MessageReceived?.Invoke(this, JsonSerializer.Serialize(new { type = "error", message = "Oversized extension host response rejected." })); continue; }
                MessageReceived?.Invoke(this, error ? JsonSerializer.Serialize(new { type = "error", message = line }) : line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { MessageReceived?.Invoke(this, JsonSerializer.Serialize(new { type = "error", message = exception.Message })); }
    }
    public async Task SendAsync(string json, CancellationToken cancellation = default)
    {
        if (_process is null || _process.HasExited) throw new InvalidOperationException("Extension host is not running.");
        await _writes.WaitAsync(cancellation);
        try { await _process.StandardInput.WriteLineAsync(json.AsMemory(), cancellation); await _process.StandardInput.FlushAsync(cancellation); }
        finally { _writes.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        _lifetime?.Cancel();
        if (_process is { } process)
        {
            try { process.StandardInput.Close(); if (!process.HasExited) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(entireProcessTree: true); } } }
            finally { process.Dispose(); _process = null; }
        }
        _lifetime?.Dispose(); _writes.Dispose();
    }
}
#endif
