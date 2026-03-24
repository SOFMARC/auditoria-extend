using Newtonsoft.Json;

namespace AuditoriaExtend.Infrastructure.Extend;

/// <summary>
/// Payload recebido no webhook da Extend para o evento extract_run.processed.
/// Documentação: https://docs.extend.ai/developers/async-processing
/// </summary>
public class ExtendWebhookPayload
{
    [JsonProperty("event")]
    public string Event { get; set; } = string.Empty;

    [JsonProperty("data")]
    public ExtendWebhookData? Data { get; set; }
}

public class ExtendWebhookData
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty; // extract_run id (RunId)

    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty; // "processed" | "failed"

    [JsonProperty("output")]
    public ExtendExtractOutput? Output { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }
}

public class ExtendExtractOutput
{
    /// <summary>Campos extraídos do documento. Cada campo tem value, confidence e metadata.</summary>
    [JsonProperty("fields")]
    public Dictionary<string, ExtendField>? Fields { get; set; }

    /// <summary>Score geral de revisão do agente (1-5).</summary>
    [JsonProperty("reviewAgentScore")]
    public int? ReviewAgentScore { get; set; }

    /// <summary>Confiança média de OCR do documento (0.0 a 1.0).</summary>
    [JsonProperty("ocrConfidence")]
    public double? OcrConfidence { get; set; }
}

public class ExtendField
{
    /// <summary>Valor extraído do campo.</summary>
    [JsonProperty("value")]
    public object? Value { get; set; }

    /// <summary>Confiança do OCR para este campo (0.0 a 1.0).</summary>
    [JsonProperty("confidence")]
    public double? Confidence { get; set; }

    /// <summary>Score de revisão do agente para este campo (1-5).</summary>
    [JsonProperty("reviewAgentScore")]
    public int? ReviewAgentScore { get; set; }

    /// <summary>Citação textual do campo no documento original.</summary>
    [JsonProperty("citation")]
    public string? Citation { get; set; }

    /// <summary>Número da página onde o campo foi encontrado (base 1).</summary>
    [JsonProperty("pageNumber")]
    public int? PageNumber { get; set; }

    /// <summary>Indica se o campo foi encontrado em uma página impressa posterior (espelho/laboratório).</summary>
    [JsonProperty("isPrintedPage")]
    public bool? IsPrintedPage { get; set; }
}
