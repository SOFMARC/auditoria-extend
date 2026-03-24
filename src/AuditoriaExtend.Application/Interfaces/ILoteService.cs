using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;

namespace AuditoriaExtend.Application.Interfaces;

public interface ILoteService
{
    Task<LoteDto> CriarLoteAsync(CriarLoteDto dto);
    Task<LoteDto?> ObterPorIdAsync(int id);
    Task<PaginatedList<LoteDto>> ListarAsync(PagedRequest request, int? filterStatus = null);
    Task<IEnumerable<LoteDto>> ListarRecentesAsync(int quantidade = 10);
    Task AtualizarStatusAsync(int id, Domain.Enums.StatusLote status, string? mensagemErro = null);
    Task IncrementarProcessadosAsync(int id);
    Task IncrementarEnviadosExtendAsync(int id);
    Task IncrementarDivergenciasAsync(int id, int quantidade = 1);
    Task IncrementarRevisaoHumanaAsync(int id, int quantidade = 1);
    Task<int> ContarTotalAsync();
    Task DeletarAsync(int id);
}
