using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Application.Interfaces;

public interface IRevisaoHumanaService
{
    Task<PaginatedList<DivergenciaAuditoriaDto>> ObterFilaAsync(PagedRequest request, SeveridadeDivergencia? severidade = null);
    Task<DivergenciaAuditoriaDto?> ObterProximaParaRevisaoAsync();
    Task RevisarAsync(RevisarDivergenciaDto dto);
    Task<PaginatedList<RevisaoHumanaDto>> ListarHistoricoAsync(PagedRequest request);
    Task<EstatisticasRevisaoDto> ObterEstatisticasAsync();

}
