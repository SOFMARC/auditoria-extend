using AutoMapper;
using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;

namespace AuditoriaExtend.Application.Services;

public class RevisaoHumanaService : IRevisaoHumanaService
{
    private readonly IRepository<DivergenciaAuditoria> _repoDivergencia;
    private readonly IRepository<RevisaoHumana> _repoRevisao;
    private readonly IMapper _mapper;

    public RevisaoHumanaService(
        IRepository<DivergenciaAuditoria> repoDivergencia,
        IRepository<RevisaoHumana> repoRevisao,
        IMapper mapper)
    {
        _repoDivergencia = repoDivergencia;
        _repoRevisao = repoRevisao;
        _mapper = mapper;
    }

    public async Task<PaginatedList<DivergenciaAuditoriaDto>> ObterFilaAsync(PagedRequest request, SeveridadeDivergencia? severidade = null)
    {
        var todas = await _repoDivergencia.GetAllAsync();
        var query = todas.Where(d => d.Status == StatusDivergencia.Pendente || d.Status == StatusDivergencia.EmRevisao);

        if (severidade.HasValue)
            query = query.Where(d => d.Severidade == severidade.Value);

        query = (request.SortBy?.ToLower(), request.SortOrder?.ToLower()) switch
        {
            ("severidade", "asc") => query.OrderBy(d => d.Severidade),
            (_, _) => query.OrderByDescending(d => d.Severidade).ThenBy(d => d.DataCriacao)
        };

        var dtos = query.Select(d => _mapper.Map<DivergenciaAuditoriaDto>(d));
        return PaginatedList<DivergenciaAuditoriaDto>.Create(dtos, request.Page, request.PageSize);
    }

    public async Task<DivergenciaAuditoriaDto?> ObterProximaParaRevisaoAsync()
    {
        var todas = await _repoDivergencia.GetAllAsync();
        var proxima = todas
            .Where(d => d.Status == StatusDivergencia.Pendente)
            .OrderByDescending(d => d.Severidade)
            .ThenBy(d => d.DataCriacao)
            .FirstOrDefault();
        return proxima == null ? null : _mapper.Map<DivergenciaAuditoriaDto>(proxima);
    }


    public async Task RevisarAsync(RevisarDivergenciaDto dto)
    {
        var revisoes = await _repoRevisao.GetAllAsync();

        var revisaoExistente = revisoes
            .FirstOrDefault(x => x.DivergenciaId == dto.DivergenciaId);

        if (revisaoExistente == null)
        {
            var novaRevisao = new RevisaoHumana
            {
                DivergenciaId = dto.DivergenciaId,
                Decisao = dto.Decisao,
                Justificativa = dto.alteracoesJson,
                ObservacaoCorrecao = dto.ObservacaoCorrecao,
                DataRevisao = DateTime.UtcNow,
                DataCriacao = DateTime.UtcNow
            };

            await _repoRevisao.AddAsync(novaRevisao);
        }
        else
        {
            revisaoExistente.Decisao = dto.Decisao;
            revisaoExistente.NomeAuditor = dto.NomeAuditor;
            revisaoExistente.Justificativa = dto.alteracoesJson;
            revisaoExistente.ObservacaoCorrecao = dto.ObservacaoCorrecao;
            revisaoExistente.DataRevisao = DateTime.UtcNow;
            revisaoExistente.DataAtualizacao = DateTime.UtcNow;

            await _repoRevisao.UpdateAsync(revisaoExistente);
        }

        await _repoRevisao.SaveChangesAsync();

        var divergencia = await _repoDivergencia.GetByIdAsync(dto.DivergenciaId);
        if (divergencia != null)
        {
            divergencia.Status = dto.Decisao;
            divergencia.DataAtualizacao = DateTime.UtcNow;
            await _repoDivergencia.UpdateAsync(divergencia);
            await _repoDivergencia.SaveChangesAsync();
        }
    }

    public async Task<PaginatedList<RevisaoHumanaDto>> ListarHistoricoAsync(PagedRequest request)
    {
        var todas = await _repoRevisao.GetAllAsync();
        var query = todas.OrderByDescending(r => r.DataRevisao);
        var dtos = query.Select(r => _mapper.Map<RevisaoHumanaDto>(r));
        return PaginatedList<RevisaoHumanaDto>.Create(dtos, request.Page, request.PageSize);
    }

    public async Task<EstatisticasRevisaoDto> ObterEstatisticasAsync()
    {
        var divergencias = await _repoDivergencia.GetAllAsync();
        var revisoes = await _repoRevisao.GetAllAsync();

        return new EstatisticasRevisaoDto
        {
            TotalPendentes = divergencias.Count(d => d.Status == StatusDivergencia.Pendente),
            TotalRevisados = revisoes.Count(),
            TotalAceitos = revisoes.Count(r => r.Decisao == StatusDivergencia.Aceita),
            TotalRejeitados = revisoes.Count(r => r.Decisao == StatusDivergencia.Rejeitada),
            TotalCorrecoesSolicitadas = revisoes.Count(r => r.Decisao == StatusDivergencia.CorrecaoSolicitada)
        };
    }
}
