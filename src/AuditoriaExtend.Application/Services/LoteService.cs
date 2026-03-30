using AutoMapper;
using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;

namespace AuditoriaExtend.Application.Services;

public class LoteService : ILoteService
{
    private readonly IRepository<Lote> _repo;
    private readonly IMapper _mapper;

    public LoteService(IRepository<Lote> repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    public async Task<LoteDto> CriarLoteAsync(CriarLoteDto dto)
    {
        var lote = _mapper.Map<Lote>(dto);
        lote.DataCriacao = DateTime.UtcNow;
        await _repo.AddAsync(lote);
        await _repo.SaveChangesAsync();
        return _mapper.Map<LoteDto>(lote);
    }

    public async Task<LoteDto?> ObterPorIdAsync(int id)
    {
        var lote = await _repo.GetByIdAsync(id);
        return lote == null ? null : _mapper.Map<LoteDto>(lote);
    }

    public async Task<PaginatedList<LoteDto>> ListarAsync(PagedRequest request, int? filterStatus = null)
    {
        var todos = await _repo.GetAllAsync();
        var query = todos.AsQueryable();

        if (filterStatus.HasValue)
            query = query.Where(l => (int)l.Status == filterStatus.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(l => l.NomeArquivo.Contains(request.Search, StringComparison.OrdinalIgnoreCase));

        query = (request.SortBy?.ToLower(), request.SortOrder?.ToLower()) switch
        {
            ("nomearquivo", "asc")      => query.OrderBy(l => l.NomeArquivo),
            ("nomearquivo", _)          => query.OrderByDescending(l => l.NomeArquivo),
            ("status", "asc")           => query.OrderBy(l => l.Status),
            ("status", _)               => query.OrderByDescending(l => l.Status),
            ("tamanhoarquivo", "asc")   => query.OrderBy(l => l.TamanhoArquivo),
            ("tamanhoarquivo", _)       => query.OrderByDescending(l => l.TamanhoArquivo),
            (_, "asc")                  => query.OrderBy(l => l.DataCriacao),
            _                           => query.OrderByDescending(l => l.DataCriacao)
        };

        var dtos = query.Select(l => _mapper.Map<LoteDto>(l));
        return PaginatedList<LoteDto>.Create(dtos, request.Page, request.PageSize);
    }

    public async Task<IEnumerable<LoteDto>> ListarRecentesAsync(int quantidade = 10)
    {
        var todos = await _repo.GetAllAsync();
        return todos.OrderByDescending(l => l.DataCriacao)
                    .Take(quantidade)
                    .Select(l => _mapper.Map<LoteDto>(l));
    }

    public async Task AtualizarStatusAsync(int id, StatusLote status, string? mensagemErro = null)
    {
        var lote = await _repo.GetByIdAsync(id);
        if (lote == null) return;
        lote.Status = status;
        lote.MensagemErro = mensagemErro;
        lote.DataAtualizacao = DateTime.UtcNow;
        if (status == StatusLote.Processando)
            lote.DataInicioProcessamento = DateTime.UtcNow;
        if (status is StatusLote.Concluido or StatusLote.Erro)
            lote.DataFimProcessamento = DateTime.UtcNow;
        await _repo.UpdateAsync(lote);
        await _repo.SaveChangesAsync();
    }

    public async Task IncrementarProcessadosAsync(int id)
    {
        var lote = await _repo.GetByIdAsync(id);
        if (lote == null) return;
        lote.QuantidadeProcessados++;
        lote.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(lote);
        await _repo.SaveChangesAsync();
    }

    /// <summary>Incrementa o contador de documentos enviados para a Extend.</summary>
    public async Task IncrementarEnviadosExtendAsync(int id)
    {
        var lote = await _repo.GetByIdAsync(id);
        if (lote == null) return;
        lote.QuantidadeEnviadosExtend++;
        lote.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(lote);
        await _repo.SaveChangesAsync();
    }

    /// <summary>Incrementa o contador de divergências detectadas no lote.</summary>
    public async Task IncrementarDivergenciasAsync(int id, int quantidade = 1)
    {
        var lote = await _repo.GetByIdAsync(id);
        if (lote == null) return;
        lote.QuantidadeDivergencias += quantidade;
        lote.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(lote);
        await _repo.SaveChangesAsync();
    }

    /// <summary>Incrementa o contador de documentos que requerem revisão humana.</summary>
    public async Task IncrementarRevisaoHumanaAsync(int id, int quantidade = 1)
    {
        var lote = await _repo.GetByIdAsync(id);
        if (lote == null) return;
        lote.QuantidadeRevisaoHumana += quantidade;
        lote.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(lote);
        await _repo.SaveChangesAsync();
    }

    public async Task<int> ContarTotalAsync()
    {
        var todos = await _repo.GetAllAsync();
        return todos.Count();
    }

    public async Task DeletarAsync(int id)
    {
        var lote = await _repo.GetByIdAsync(id);
        if (lote == null) return;
        await _repo.DeleteAsync(lote);
        await _repo.SaveChangesAsync();
    }
}
