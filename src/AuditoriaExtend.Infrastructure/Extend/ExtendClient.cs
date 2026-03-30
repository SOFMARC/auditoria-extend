using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Infrastructure.Configuration;

namespace AuditoriaExtend.Infrastructure.Extend;

/// <summary>
/// Implementação do cliente HTTP para a API Extend.
/// Documentação: https://docs.extend.ai/developers/api-reference
/// Implementa a interface IExtendClient definida na camada Application (Clean Architecture).
/// </summary>
public class ExtendClient : IExtendClient
{
    private readonly HttpClient _http;
    private readonly ExtendOptions _options;
    private readonly ILogger<ExtendClient> _logger;

    private const string ApiVersion = "2026-02-09";

    public ExtendClient(HttpClient http, IOptions<ExtendOptions> options, ILogger<ExtendClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress = new Uri("https://api.extend.ai/");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
        _http.DefaultRequestHeaders.Add("x-extend-api-version", ApiVersion);
    }

    private static string ObterContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".pdf" => "application/pdf",
            ".tif" => "image/tiff",
            ".tiff" => "image/tiff",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    /// <inheritdoc/>
    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        _logger.LogInformation("Extend: enviando arquivo '{FileName}' para upload", fileName);

        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);        
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ObterContentType(fileName));

        content.Add(streamContent, "file", fileName);

        var response = await _http.PostAsync("files/upload", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Extend: falha no upload do arquivo '{FileName}'. Status={Status} Body={Body}",
                fileName, response.StatusCode, body);
            throw new ExtendApiException($"Falha no upload para Extend: {response.StatusCode} — {body}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var obj = JObject.Parse(json);
        var fileId = obj["id"]?.ToString()
            ?? throw new ExtendApiException("Extend não retornou 'id' no upload do arquivo.");

        _logger.LogInformation("Extend: arquivo '{FileName}' enviado com sucesso. FileId={FileId}", fileName, fileId);
        return fileId;
    }

    /// <inheritdoc/>
    public async Task<string> IniciarExtracaoAsync(string fileId, string extractorId, CancellationToken ct = default)
    {
        _logger.LogInformation("Extend: iniciando extração. FileId={FileId} ExtractorId={ExtractorId}",
            fileId, extractorId);

        var payload = new
        {
            extractor = new{id = extractorId}, file = new{id = fileId}
        };

        var json = JsonConvert.SerializeObject(payload);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("extract_runs", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Extend: falha ao iniciar extração. FileId={FileId} Status={Status} Body={Body}",
                fileId, response.StatusCode, body);
            throw new ExtendApiException($"Falha ao iniciar extração na Extend: {response.StatusCode} — {body}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var obj = JObject.Parse(responseJson);
        var runId = obj["id"]?.ToString()
            ?? throw new ExtendApiException("Extend não retornou 'id' no extract_run.");

        _logger.LogInformation("Extend: extração iniciada com sucesso. RunId={RunId}", runId);
        return runId;
    }
}
