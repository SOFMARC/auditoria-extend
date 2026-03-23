using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Application.Interfaces;

public interface IDivergenciaService
{
    Task<DivergenciaAuditoriaDto> CriarAsync(int documentoId, TipoDivergencia tipo,
        SeveridadeDivergencia severidade, string descricao,
        int? atendimentoAgrupadoId = null, double? valorConfianca = null,
        string? campoAfetado = null, string? valorEncontrado = null, string? valorEsperado = null);

    Task<DivergenciaAuditoriaDto?> ObterPorIdAsync(int id);
    Task<PaginatedList<DivergenciaAuditoriaDto>> ListarAsync(PagedRequest request,
        int? filterStatus = null, int? filterSeveridade = null, int? filterTipo = null, int? loteId = null);
    Task<IEnumerable<DivergenciaAuditoriaDto>> ListarPorDocumentoAsync(int documentoId);
    Task<IEnumerable<DivergenciaAuditoriaDto>> ListarPendentesPorSeveridadeAsync(SeveridadeDivergencia? severidade = null);
    Task AtualizarStatusAsync(int id, StatusDivergencia status);
    Task<int> ContarPendentesAsync();
}
