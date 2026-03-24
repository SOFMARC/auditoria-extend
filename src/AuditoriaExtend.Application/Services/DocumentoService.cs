using AutoMapper;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;

namespace AuditoriaExtend.Application.Services;

public class DocumentoService : IDocumentoService
{
    private readonly IRepository<Documento> _repo;
    private readonly IMapper _mapper;

    public DocumentoService(IRepository<Documento> repo, IMapper mapper)
    {
        _repo = repo;
        _mapper = mapper;
    }

    public async Task<DocumentoDto> CriarAsync(int loteId, string nomeArquivo, string caminhoArquivo)
    {
        var doc = new Documento
        {
            LoteId = loteId,
            NomeArquivo = nomeArquivo,
            CaminhoArquivo = caminhoArquivo,
            Status = StatusDocumento.Pendente,
            DataCriacao = DateTime.UtcNow
        };
        await _repo.AddAsync(doc);
        await _repo.SaveChangesAsync();
        return _mapper.Map<DocumentoDto>(doc);
    }

    public async Task<DocumentoDto?> ObterPorIdAsync(int id)
    {
        var doc = await _repo.GetByIdAsync(id);
        return doc == null ? null : _mapper.Map<DocumentoDto>(doc);
    }

    public async Task<IEnumerable<DocumentoDto>> ListarPorLoteAsync(int loteId)
    {
        var todos = await _repo.GetAllAsync();
        return todos.Where(d => d.LoteId == loteId)
                    .Select(d => _mapper.Map<DocumentoDto>(d));
    }

    public async Task AtualizarTipoAsync(int id, TipoDocumento tipo)
    {
        var doc = await _repo.GetByIdAsync(id);
        if (doc == null) return;
        doc.TipoDocumento = tipo;
        doc.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(doc);
        await _repo.SaveChangesAsync();
    }

    public async Task AtualizarStatusAsync(int id, StatusDocumento status, string? mensagemErro = null)
    {
        var doc = await _repo.GetByIdAsync(id);
        if (doc == null) return;
        doc.Status = status;
        doc.MensagemErro = mensagemErro;
        doc.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(doc);
        await _repo.SaveChangesAsync();
    }

    /// <summary>
    /// Persiste os dados retornados pelo webhook da Extend após a extração.
    /// Atualiza: DadosExtraidos (JSON dos campos), ConfiancaOcr, ReviewAgentScore, ExtractorId.
    /// Status é atualizado para Processado.
    /// </summary>
    public async Task SalvarDadosExtracaoAsync(int id, string dadosJson, double confianca, string extractorId, int? reviewAgentScore = null)
    {
        var doc = await _repo.GetByIdAsync(id);
        if (doc == null) return;
        doc.DadosExtraidos = dadosJson;
        doc.ConfiancaOcr = confianca;
        doc.ExtractorId = extractorId;
        doc.ReviewAgentScore = reviewAgentScore;
        doc.Status = StatusDocumento.Processado;
        doc.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(doc);
        await _repo.SaveChangesAsync();
    }

    /// <summary>
    /// Salva o FileId e RunId retornados pela Extend após o upload e início da extração.
    /// Atualiza o status do documento para AguardandoExtend.
    /// </summary>
    public async Task SalvarExtendRunIdAsync(int id, string extendFileId, string extendRunId, string extractorId)
    {
        var doc = await _repo.GetByIdAsync(id);
        if (doc == null) return;
        doc.ExtendFileId = extendFileId;
        doc.ExtendRunId = extendRunId;
        doc.ExtractorId = extractorId;
        doc.Status = StatusDocumento.AguardandoExtend;
        doc.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(doc);
        await _repo.SaveChangesAsync();
    }

    public async Task VincularAtendimentoAsync(int id, int atendimentoAgrupadoId)
    {
        var doc = await _repo.GetByIdAsync(id);
        if (doc == null) return;
        doc.AtendimentoAgrupadoId = atendimentoAgrupadoId;
        doc.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(doc);
        await _repo.SaveChangesAsync();
    }

    /// <summary>
    /// Atualiza as flags de auditoria do documento: OrigemSuspeita e RevisaoHumanaNecessaria.
    /// Chamado pelo AuditoriaRegraService após aplicar as regras C e D.
    /// </summary>
    public async Task AtualizarFlagsAuditoriaAsync(int id, bool origemSuspeita, bool revisaoHumanaNecessaria)
    {
        var doc = await _repo.GetByIdAsync(id);
        if (doc == null) return;
        doc.OrigemSuspeita = origemSuspeita;
        doc.RevisaoHumanaNecessaria = revisaoHumanaNecessaria;
        doc.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(doc);
        await _repo.SaveChangesAsync();
    }
}
