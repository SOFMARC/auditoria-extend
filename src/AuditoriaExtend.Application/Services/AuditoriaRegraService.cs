using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;
using AuditoriaExtend.Domain.Entities;

namespace AuditoriaExtend.Application.Services;

public class AuditoriaRegraService : IAuditoriaRegraService
{
    private readonly IRepository<Documento> _repoDoc;
    private readonly IDivergenciaService _divService;

    // Limiar de confiança abaixo do qual gera divergência
    private const double LimiarConfiancaBaixa = 0.75;

    public AuditoriaRegraService(IRepository<Documento> repoDoc, IDivergenciaService divService)
    {
        _repoDoc = repoDoc;
        _divService = divService;
    }

    public async Task<int> AuditarDocumentoAsync(int documentoId)
    {
        var doc = await _repoDoc.GetByIdAsync(documentoId);
        if (doc == null) return 0;

        int divergencias = 0;

        // Regra 1: Confiança de OCR baixa
        if (doc.ConfiancaOcr > 0 && doc.ConfiancaOcr < LimiarConfiancaBaixa)
        {
            await _divService.CriarAsync(
                documentoId: documentoId,
                tipo: TipoDivergencia.ConfiancaBaixa,
                severidade: doc.ConfiancaOcr < 0.5 ? SeveridadeDivergencia.Alta : SeveridadeDivergencia.Media,
                descricao: $"Confiança de OCR abaixo do limiar: {doc.ConfiancaOcr:P0}",
                valorConfianca: doc.ConfiancaOcr,
                campoAfetado: "OCR",
                valorEncontrado: doc.ConfiancaOcr.ToString("F2"),
                valorEsperado: $">= {LimiarConfiancaBaixa:F2}"
            );
            divergencias++;
        }

        // Regra 2: Tipo de documento desconhecido
        if (doc.TipoDocumento == TipoDocumento.Desconhecido)
        {
            await _divService.CriarAsync(
                documentoId: documentoId,
                tipo: TipoDivergencia.CampoObrigatorioAusente,
                severidade: SeveridadeDivergencia.Media,
                descricao: "Tipo de documento não identificado automaticamente.",
                campoAfetado: "TipoDocumento"
            );
            divergencias++;
        }

        // Regra 3: Dados extraídos ausentes
        if (string.IsNullOrWhiteSpace(doc.DadosExtraidos) && doc.Status == StatusDocumento.Processado)
        {
            await _divService.CriarAsync(
                documentoId: documentoId,
                tipo: TipoDivergencia.CampoObrigatorioAusente,
                severidade: SeveridadeDivergencia.Alta,
                descricao: "Nenhum dado foi extraído do documento.",
                campoAfetado: "DadosExtraidos"
            );
            divergencias++;
        }

        return divergencias;
    }

    public async Task<int> DetectarDuplicidadesAsync(int loteId)
    {
        var todos = await _repoDoc.GetAllAsync();
        var docsLote = todos.Where(d => d.LoteId == loteId).ToList();

        var grupos = docsLote
            .GroupBy(d => d.NomeArquivo.ToLower())
            .Where(g => g.Count() > 1)
            .ToList();

        int divergencias = 0;
        foreach (var grupo in grupos)
        {
            foreach (var doc in grupo.Skip(1)) // O primeiro é o original
            {
                await _divService.CriarAsync(
                    documentoId: doc.Id,
                    tipo: TipoDivergencia.DuplicidadeDetectada,
                    severidade: SeveridadeDivergencia.Alta,
                    descricao: $"Documento duplicado detectado: '{doc.NomeArquivo}'",
                    campoAfetado: "NomeArquivo",
                    valorEncontrado: doc.NomeArquivo
                );
                divergencias++;
            }
        }
        return divergencias;
    }
}
