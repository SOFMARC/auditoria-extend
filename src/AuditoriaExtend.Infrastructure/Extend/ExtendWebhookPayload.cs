using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuditoriaExtend.Infrastructure.Extend;

/// <summary>
/// Payload raiz recebido no webhook da Extend.
/// Estrutura real confirmada em produção:
/// {
///   "eventId": "evt_...",
///   "eventType": "extract_run.processed",
///   "payload": {
///     "id": "exr_...",
///     "status": "PROCESSED",
///     "output": {
///       "value": { "numero_carteira": "...", "itens_realizados": [...] },
///       "metadata": { "numero_carteira": { "confidence": 0.98, "reviewAgentScore": 5 } }
///     }
///   }
/// }
/// </summary>
public class ExtendWebhookPayload
{
    [JsonProperty("eventId")]
    public string EventId { get; set; } = string.Empty;

    /// <summary>Tipo do evento: "extract_run.processed" | "extract_run.failed"</summary>
    [JsonProperty("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonProperty("payload")]
    public ExtendWebhookData? Payload { get; set; }
}

public class ExtendWebhookData
{
    /// <summary>Id do extract_run — corresponde ao ExtendRunId salvo no banco.</summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Status: "PROCESSED" | "FAILED" (maiúsculo conforme API real).</summary>
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("output")]
    public ExtendExtractOutput? Output { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }

    [JsonProperty("extractor")]
    public ExtendExtractorInfo? Extractor { get; set; }
}

public class ExtendExtractorInfo
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Output da extração retornado pela Extend.
/// Os valores extraídos ficam em "value" (objeto plano) e os metadados de confiança em "metadata".
/// </summary>
public class ExtendExtractOutput
{
    /// <summary>
    /// Valores extraídos dos campos do documento.
    /// Objeto plano onde cada chave é o nome do campo configurado no extractor.
    /// Exemplo: { "numero_carteira": "402865000178", "data_solicitacao": "2025-01-18" }
    /// </summary>
    [JsonProperty("value")]
    public JObject? Value { get; set; }

    /// <summary>
    /// Metadados de confiança por campo.
    /// Cada chave corresponde a um campo em "value".
    /// Exemplo: { "numero_carteira": { "confidence": 0.98, "reviewAgentScore": 5, "citation": "..." } }
    /// </summary>
    [JsonProperty("metadata")]
    public JObject? Metadata { get; set; }

    /// <summary>Score geral de revisão do agente (1-5), se disponível no nível do output.</summary>
    [JsonProperty("reviewAgentScore")]
    public int? ReviewAgentScore { get; set; }

    /// <summary>Confiança média de OCR do documento (0.0 a 1.0), se disponível no nível do output.</summary>
    [JsonProperty("ocrConfidence")]
    public double? OcrConfidence { get; set; }
}

/// <summary>
/// Metadados de um campo individual retornado pela Extend.
/// </summary>
public class ExtendFieldMetadata
{
    [JsonProperty("confidence")]
    public double? Confidence { get; set; }

    [JsonProperty("reviewAgentScore")]
    public int? ReviewAgentScore { get; set; }

    [JsonProperty("citation")]
    public string? Citation { get; set; }

    [JsonProperty("pageNumber")]
    public int? PageNumber { get; set; }

    /// <summary>
    /// Indica se o campo foi encontrado em uma página impressa posterior
    /// (espelho de atendimento/laboratório) — usado na Regra D (Origem Suspeita).
    /// </summary>
    [JsonProperty("isPrintedPage")]
    public bool? IsPrintedPage { get; set; }
}
