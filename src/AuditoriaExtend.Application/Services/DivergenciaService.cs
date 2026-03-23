using AutoMapper;
using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;

namespace AuditoriaExtend.Application.Services;

public class DivergenciaService : IDivergenciaService
{
    private readonly IRepository<DivergenciaAuditoria> _repo;
    private readonly IRepository<Documento> _repoDoc;
    private readonly IMapper _mapper;

    public DivergenciaService(IRepository<DivergenciaAuditoria> repo,
        IRepository<Documento> repoDoc, IMapper mapper)
    {
        _repo = repo;
        _repoDoc = repoDoc;
        _mapper = mapper;
    }

    public async Task<DivergenciaAuditoriaDto> CriarAsync(int documentoId, TipoDivergencia tipo,
        SeveridadeDivergencia severidade, string descricao,
        int? atendimentoAgrupadoId = null, double? valorConfianca = null,
        string? campoAfetado = null, string? valorEncontrado = null, string? valorEsperado = null)
    {
        var div = new DivergenciaAuditoria
        {
            DocumentoId = documentoId,
            AtendimentoAgrupadoId = atendimentoAgrupadoId,
            Tipo = tipo,
            Severidade = severidade,
            Status = StatusDivergencia.Pendente,
            Descricao = descricao,
            ValorConfianca = valorConfianca,
            CampoAfetado = campoAfetado,
            ValorEncontrado = valorEncontrado,
            ValorEsperado = valorEsperado,
            DataCriacao = DateTime.UtcNow
        };
        await _repo.AddAsync(div);
        await _repo.SaveChangesAsync();
        return _mapper.Map<DivergenciaAuditoriaDto>(div);
    }

    public async Task<DivergenciaAuditoriaDto?> ObterPorIdAsync(int id)
    {
        var div = await _repo.GetByIdAsync(id);
        if (div == null) return null;
        // Enrich with document name
        var doc = await _repoDoc.GetByIdAsync(div.DocumentoId);
        if (doc != null) div.Documento = doc;
        return _mapper.Map<DivergenciaAuditoriaDto>(div);
    }

    public async Task<PaginatedList<DivergenciaAuditoriaDto>> ListarAsync(PagedRequest request,
        int? filterStatus = null, int? filterSeveridade = null, int? filterTipo = null, int? loteId = null)
    {
        var todos = await _repo.GetAllAsync();
        var query = todos.AsQueryable();

        if (filterStatus.HasValue)
            query = query.Where(d => (int)d.Status == filterStatus.Value);
        if (filterSeveridade.HasValue)
            query = query.Where(d => (int)d.Severidade == filterSeveridade.Value);
        if (filterTipo.HasValue)
            query = query.Where(d => (int)d.Tipo == filterTipo.Value);

        query = (request.SortBy?.ToLower(), request.SortOrder?.ToLower()) switch
        {
            ("severidade", "asc") => query.OrderBy(d => d.Severidade),
            ("severidade", _) => query.OrderByDescending(d => d.Severidade),
            ("tipo", "asc") => query.OrderBy(d => d.Tipo),
            ("tipo", _) => query.OrderByDescending(d => d.Tipo),
            (_, "asc") => query.OrderBy(d => d.DataCriacao),
            _ => query.OrderByDescending(d => d.DataCriacao)
        };

        var dtos = query.Select(d => _mapper.Map<DivergenciaAuditoriaDto>(d));
        return PaginatedList<DivergenciaAuditoriaDto>.Create(dtos, request.Page, request.PageSize);
    }

    public async Task<IEnumerable<DivergenciaAuditoriaDto>> ListarPorDocumentoAsync(int documentoId)
    {
        var todos = await _repo.GetAllAsync();
        return todos.Where(d => d.DocumentoId == documentoId)
                    .Select(d => _mapper.Map<DivergenciaAuditoriaDto>(d));
    }

    public async Task<IEnumerable<DivergenciaAuditoriaDto>> ListarPendentesPorSeveridadeAsync(SeveridadeDivergencia? severidade = null)
    {
        var todos = await _repo.GetAllAsync();
        var query = todos.Where(d => d.Status == StatusDivergencia.Pendente);
        if (severidade.HasValue)
            query = query.Where(d => d.Severidade == severidade.Value);
        return query.OrderByDescending(d => d.Severidade)
                    .ThenBy(d => d.DataCriacao)
                    .Select(d => _mapper.Map<DivergenciaAuditoriaDto>(d));
    }

    public async Task AtualizarStatusAsync(int id, StatusDivergencia status)
    {
        var div = await _repo.GetByIdAsync(id);
        if (div == null) return;
        div.Status = status;
        div.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(div);
        await _repo.SaveChangesAsync();
    }

    public async Task<int> ContarPendentesAsync()
    {
        var todos = await _repo.GetAllAsync();
        return todos.Count(d => d.Status == StatusDivergencia.Pendente);
    }
}
