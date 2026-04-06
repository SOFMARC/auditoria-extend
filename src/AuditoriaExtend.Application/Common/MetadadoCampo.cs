namespace AuditoriaExtend.Application.Common;

/// <summary>
/// Representa os metadados de um campo extraído pela Extend.
/// Compatível com o formato atual: citations, ocrConfidence, logprobsConfidence.
/// </summary>
public sealed class MetadadoCampo
{
    /// <summary>Nome do campo no JSON normalizado (camelCase).</summary>
    public string NomeCampo { get; init; } = string.Empty;

    /// <summary>Valor textual do campo (já extraído do wrapper {value, confidence}).</summary>
    public string? Value { get; init; }

    /// <summary>
    /// Confiança OCR do campo (0–1). Fonte primária de qualidade.
    /// Mapeado de ocrConfidence no payload atual da Extend.
    /// </summary>
    public double? OcrConfidence { get; init; }

    /// <summary>
    /// Confiança por log-probs do modelo (0–1). Sinal complementar.
    /// Mapeado de logprobsConfidence no payload atual da Extend.
    /// </summary>
    public double? LogprobsConfidence { get; init; }

    /// <summary>
    /// Score do agente de revisão (0–1). Legado — pode não existir no payload atual.
    /// </summary>
    public double? ReviewAgentScore { get; init; }

    /// <summary>
    /// Citações do campo (trechos do documento que embasaram a extração).
    /// Array de strings no payload atual da Extend.
    /// </summary>
    public IReadOnlyList<string> Citations { get; init; } = Array.Empty<string>();

    /// <summary>Número da página onde o campo foi encontrado.</summary>
    public int? PageNumber { get; init; }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de avaliação de qualidade
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Confiança efetiva do campo: prioriza OcrConfidence, fallback para LogprobsConfidence,
    /// fallback para ReviewAgentScore. Retorna null se nenhum estiver disponível.
    /// </summary>
    public double? ConfidenceEfetiva =>
        OcrConfidence ?? LogprobsConfidence ?? ReviewAgentScore;

    /// <summary>
    /// Indica se o campo tem confiança baixa (abaixo do limiar informado).
    /// </summary>
    public bool TemConfiancaBaixa(double limiar = 0.70) =>
        ConfidenceEfetiva.HasValue && ConfidenceEfetiva.Value < limiar;

    /// <summary>
    /// Indica se o campo possui citação de suporte no documento.
    /// </summary>
    public bool TemCitacao => Citations.Count > 0;
}
