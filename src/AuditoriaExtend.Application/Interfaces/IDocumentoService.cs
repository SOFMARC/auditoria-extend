using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Application.Interfaces;

public interface IDocumentoService
{
    Task<DocumentoDto> CriarAsync(int loteId, string nomeArquivo, string caminhoArquivo);
    Task<DocumentoDto?> ObterPorIdAsync(int id);
    Task<IEnumerable<DocumentoDto>> ListarPorLoteAsync(int loteId);
    Task AtualizarTipoAsync(int id, TipoDocumento tipo);
    Task AtualizarStatusAsync(int id, StatusDocumento status, string? mensagemErro = null);

    /// <summary>
    /// Persiste os dados retornados pelo webhook da Extend:
    /// JSON dos campos extraídos, confiança OCR, reviewAgentScore e extractorId.
    /// </summary>
    Task SalvarDadosExtracaoAsync(int id, string dadosJson, double confianca, string extractorId, int? reviewAgentScore = null);

    /// <summary>Salva o ExtendFileId e ExtendRunId, atualiza status para AguardandoExtend.</summary>
    Task SalvarExtendRunIdAsync(int id, string extendFileId, string extendRunId, string extractorId);

    Task VincularAtendimentoAsync(int id, int atendimentoAgrupadoId);

    /// <summary>Atualiza as flags de auditoria: OrigemSuspeita e RevisaoHumanaNecessaria.</summary>
    Task AtualizarFlagsAuditoriaAsync(int id, bool origemSuspeita, bool revisaoHumanaNecessaria);
}
