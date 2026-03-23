using System.IO.Compression;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Application.Services;

public class ImportacaoService : IImportacaoService
{
    private readonly ILoteService _loteService;
    private readonly IDocumentoService _documentoService;
    private readonly IAuditoriaRegraService _auditoriaService;
    private readonly string _uploadRoot;

    private static readonly string[] ExtensoesPdf = { ".pdf", ".tiff", ".tif", ".jpg", ".jpeg", ".png" };

    public ImportacaoService(ILoteService loteService, IDocumentoService documentoService,
        IAuditoriaRegraService auditoriaService)
    {
        _loteService = loteService;
        _documentoService = documentoService;
        _auditoriaService = auditoriaService;
        _uploadRoot = Path.Combine("wwwroot", "uploads", "lotes");
    }

    public async Task<LoteDto> ReceberArquivoAsync(Stream stream, string nomeArquivo, long tamanho)
    {
        Directory.CreateDirectory(_uploadRoot);
        var nomeUnico = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Path.GetFileName(nomeArquivo)}";
        var caminho = Path.Combine(_uploadRoot, nomeUnico);

        await using (var fs = File.Create(caminho))
            await stream.CopyToAsync(fs);

        var dto = new CriarLoteDto
        {
            NomeArquivo = nomeArquivo,
            CaminhoArquivo = caminho,
            TamanhoArquivo = tamanho
        };
        return await _loteService.CriarLoteAsync(dto);
    }

    public async Task ProcessarLoteAsync(int loteId)
    {
        var lote = await _loteService.ObterPorIdAsync(loteId)
            ?? throw new InvalidOperationException($"Lote {loteId} não encontrado.");

        await _loteService.AtualizarStatusAsync(loteId, StatusLote.Processando);

        try
        {
            var pastaExtracao = Path.Combine(_uploadRoot, $"extracao_{loteId}");
            Directory.CreateDirectory(pastaExtracao);

            ZipFile.ExtractToDirectory(lote.NomeArquivo, pastaExtracao, overwriteFiles: true);

            var arquivos = Directory.GetFiles(pastaExtracao, "*.*", SearchOption.AllDirectories)
                .Where(f => ExtensoesPdf.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            // Atualiza quantidade de documentos
            var loteEntity = await _loteService.ObterPorIdAsync(loteId);

            foreach (var arquivo in arquivos)
            {
                var doc = await _documentoService.CriarAsync(loteId, Path.GetFileName(arquivo), arquivo);

                // Classifica tipo pelo nome
                var tipo = ClassificarTipo(Path.GetFileName(arquivo));
                await _documentoService.AtualizarTipoAsync(doc.Id, tipo);

                // Auditoria básica (confiança simulada sem API real)
                await _auditoriaService.AuditarDocumentoAsync(doc.Id);
                await _loteService.IncrementarProcessadosAsync(loteId);
            }

            await _auditoriaService.DetectarDuplicidadesAsync(loteId);
            await _loteService.AtualizarStatusAsync(loteId, StatusLote.Concluido);
        }
        catch (Exception ex)
        {
            await _loteService.AtualizarStatusAsync(loteId, StatusLote.Erro, ex.Message);
            throw;
        }
    }

    private static TipoDocumento ClassificarTipo(string nomeArquivo)
    {
        var nome = nomeArquivo.ToLower();
        if (nome.Contains("guia") || nome.Contains("sadt") || nome.Contains("sp_")) return TipoDocumento.GuiaSPSADT;
        if (nome.Contains("pedido") || nome.Contains("solicitacao")) return TipoDocumento.PedidoMedico;
        if (nome.Contains("laudo")) return TipoDocumento.Laudo;
        if (nome.Contains("receita")) return TipoDocumento.Receita;
        return TipoDocumento.Desconhecido;
    }
}
