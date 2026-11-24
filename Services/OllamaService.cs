using System;
using System.Collections.Generic;
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
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResult>(Json, cts.Token)
                     ?? throw new InvalidOperationException("Ollama returned an empty answer.");

        if (result.EvalDuration <= 0)
            throw new InvalidOperationException("Ollama did not report a generation time.");

        return result.EvalCount / (result.EvalDuration / 1e9);
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
