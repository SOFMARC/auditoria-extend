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
    private readonly IRepository<DivergenciaAuditoria> _repoDivergencia;
    private readonly IRepository<RevisaoHumana> _repoRevisao;
        private readonly IMapper _mapper;
    private readonly IRepository<Documento> _repoDocumento;


    public LoteService(IRepository<Lote> repo, IMapper mapper, IRepository<Documento> repoDoc, IRepository<DivergenciaAuditoria> repod, IRepository<RevisaoHumana> repoRevisao)
    {
        _repo = repo;
        _repoDivergencia = repod;
        _repoRevisao = repoRevisao;

        _mapper = mapper;
        _repoDocumento = repoDoc;

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
        if (lote == null)
            return null;

        var dto = _mapper.Map<LoteDto>(lote);

        await PreencherInfosAtualizadasAsync(dto);

        return dto;
    }

    private async Task PreencherInfosAtualizadasAsync(LoteDto lote)
    {
        var documentos = (await _repoDocumento.GetAllAsync())
            .Where(d => d.LoteId == lote.Id)
            .ToList();

        var documentoIds = documentos
            .Select(d => d.Id)
            .ToHashSet();

        var divergencias = (await _repoDivergencia.GetAllAsync())
            .Where(d => documentoIds.Contains(d.DocumentoId) && d.Status == StatusDivergencia.Pendente)
            .ToList();

        var divergenciaIds = divergencias
            .Select(d => d.Id)
            .ToHashSet();

        var revisoes = (await _repoRevisao.GetAllAsync())
            .Where(r => divergenciaIds.Contains(r.DivergenciaId))
            .ToList();

        lote.QuantidadeDocumentos = documentos.Count;
        lote.QuantidadeDivergencias = divergencias.Count;
        lote.QuantidadeRevisaoHumana = revisoes.Count;

        // ajuste o enum/status conforme o seu projeto
        lote.QuantidadeProcessados = documentos.Count(d => d.Status == StatusDocumento.Processado);
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

    public async Task<IEnumerable<LoteDto>> ListarRecentesComInfosAtualizadasAsync(int quantidade)
    {
        var lotes = (await ListarRecentesAsync(quantidade)).ToList();

        foreach (var lote in lotes)
            await PreencherInfosAtualizadasAsync(lote);

        return lotes;
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

    /// <summary>Define o total de documentos encontrados no ZIP ao iniciar o processamento do lote.</summary>
    public async Task DefinirQuantidadeDocumentosAsync(int id, int quantidade)
    {
        var lote = await _repo.GetByIdAsync(id);
        if (lote == null) return;
        lote.QuantidadeDocumentos = quantidade;
        lote.DataAtualizacao = DateTime.UtcNow;
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
