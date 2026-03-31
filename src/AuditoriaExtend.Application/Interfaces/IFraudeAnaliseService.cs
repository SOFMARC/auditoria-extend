using AuditoriaExtend.Domain.Entities;

namespace AuditoriaExtend.Application.Interfaces;

/// <summary>
/// Serviço de análise antifraude via LLM para lotes sem divergências pendentes.
/// </summary>
public interface IFraudeAnaliseService
{
    /// <summary>
    /// Verifica se o lote está elegível para análise antifraude:
    /// - Status Concluido
    /// - Sem divergências Pendente ou EmRevisao
    /// - Sem análise já existente (Concluido ou Processando)
    /// </summary>
    Task<bool> LoteElegivelParaAnaliseAsync(int loteId);

    /// <summary>
    /// Executa a análise antifraude do lote via LLM e persiste o resultado.
    /// </summary>
    Task<ResultadoFraudeAnalise> AnalisarLoteAsync(int loteId, CancellationToken ct = default);

    /// <summary>
    /// Retorna o resultado de análise mais recente para um lote.
    /// </summary>
    Task<ResultadoFraudeAnalise?> ObterResultadoAsync(int loteId);
}
