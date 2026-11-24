using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiBoxDashboard.Models;

namespace AiBoxDashboard.Services;

/// <summary>Talks to an Ollama server over its HTTP API.</summary>
public sealed class OllamaService : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;

    public OllamaService(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Ollama address must look like http://host:11434.");

        _http = new HttpClient { BaseAddress = uri, Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<string> GetVersionAsync(CancellationToken ct)
        => (await GetAsync<OllamaVersion>("api/version", ct)).Version;

    public async Task<IReadOnlyList<OllamaModel>> GetModelsAsync(CancellationToken ct)
        => (await GetAsync<OllamaTagList>("api/tags", ct)).Models;

    public async Task<IReadOnlyList<OllamaRunningModel>> GetRunningAsync(CancellationToken ct)
        => (await GetAsync<OllamaRunningList>("api/ps", ct)).Models;

    /// <summary>
    /// Generates a short text and returns tokens per second. Loads the model if it is not loaded yet.
    /// </summary>
    public async Task<double> MeasureSpeedAsync(string model, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(3));

        var request = new
        {
            model,
            prompt = "Explain in a few sentences how a hash map works.",
            stream = false,
            options = new { num_predict = 128 },
        };

        using var response = await _http.PostAsJsonAsync("api/generate", request, Json, cts.Token);
        await EnsureOkAsync(response, cts.Token);
        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResult>(Json, cts.Token)
                     ?? throw new InvalidOperationException("Ollama returned an empty answer.");

        if (result.EvalDuration <= 0)
            throw new InvalidOperationException("Ollama did not report a generation time.");

        return result.EvalCount / (result.EvalDuration / 1e9);
    }

    /// <summary>Loads a model into memory and keeps it for Ollama's usual keep-alive time.</summary>
    public Task LoadAsync(string model, CancellationToken ct)
        => GenerateWithoutPromptAsync(new { model, stream = false }, TimeSpan.FromMinutes(5), ct);

    /// <summary>Asks Ollama to drop a model from memory now.</summary>
    public Task UnloadAsync(string model, CancellationToken ct)
        => GenerateWithoutPromptAsync(new { model, stream = false, keep_alive = 0 }, TimeSpan.FromSeconds(30), ct);

    /// <summary>Removes a model from disk.</summary>
    public async Task DeleteAsync(string model, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/delete")
        {
            Content = JsonContent.Create(new { model }, options: Json),
        };
        using var response = await _http.SendAsync(request, cts.Token);
        await EnsureOkAsync(response, cts.Token);
    }

    /// <summary>
    /// Downloads a model, reporting progress as it goes. Cancelling keeps the parts already downloaded,
    /// so a later pull of the same model resumes.
    /// </summary>
    public async Task PullAsync(string model, IProgress<PullProgress> progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/pull")
        {
            Content = JsonContent.Create(new { model, stream = true }, options: Json),
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureOkAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var layers = new Dictionary<string, (long Completed, long Total)>();
        var succeeded = false;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;
            var update = JsonSerializer.Deserialize<OllamaPullLine>(line, Json);
            if (update is null) continue;
            if (!string.IsNullOrEmpty(update.Error))
                throw new InvalidOperationException(update.Error);

            if (update is { Digest: { } digest, Total: > 0 })
                layers[digest] = (update.Completed ?? 0, update.Total.Value);

            succeeded = update.Status == "success";
            progress.Report(new PullProgress(update.Status ?? "", OverallFraction(layers)));
        }

        if (!succeeded)
            throw new InvalidOperationException("The download stopped before Ollama reported success.");
    }

    /// <summary>Share of all layers seen so far that is downloaded; null before the first size arrives.</summary>
    public static double? OverallFraction(IReadOnlyDictionary<string, (long Completed, long Total)> layers)
    {
        var total = layers.Values.Sum(l => l.Total);
        return total > 0 ? Math.Clamp((double)layers.Values.Sum(l => l.Completed) / total, 0, 1) : null;
    }

    private async Task GenerateWithoutPromptAsync(object request, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var response = await _http.PostAsJsonAsync("api/generate", request, Json, cts.Token);
            await EnsureOkAsync(response, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Ollama did not answer within {timeout.TotalSeconds:0} s.");
        }
    }

    /// <summary>Turns Ollama's <c>{"error": "..."}</c> answers into exceptions that carry its message.</summary>
    private static async Task EnsureOkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        string? message = null;
        try
        {
            message = (await response.Content.ReadFromJsonAsync<OllamaError>(Json, ct))?.Error;
        }
        catch (JsonException)
        {
            // Not a JSON error body; fall back to the status code.
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(message) ? $"Ollama answered {(int)response.StatusCode}." : message);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await _http.GetFromJsonAsync<T>(path, Json, cts.Token)
                   ?? throw new InvalidOperationException("Ollama returned an empty answer.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Ollama did not answer within 5 s.");
        }
    }

    public void Dispose() => _http.Dispose();
}
