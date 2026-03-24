using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AuditoriaExtend.Application.Configuration;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Application.Services;

/// <summary>
/// Serviço de importação de lotes.
/// Fluxo: recebe ZIP → extrai PDFs → classifica tipo → envia cada PDF para a Extend
/// → salva RunId → status AguardandoExtend.
/// O processamento das regras de auditoria ocorre APENAS após o retorno do webhook da Extend.
/// </summary>
public class ImportacaoService : IImportacaoService
{
    private readonly ILoteService _loteService;
    private readonly IDocumentoService _documentoService;
    private readonly IExtendClient _extendClient;
    private readonly ExtendClientOptions _extendOptions;
    private readonly ILogger<ImportacaoService> _logger;
    private readonly string _uploadRoot;

    private static readonly string[] ExtensoesSuportadas = { ".pdf", ".tiff", ".tif", ".jpg", ".jpeg", ".png" };

    public ImportacaoService(
        ILoteService loteService,
        IDocumentoService documentoService,
        IExtendClient extendClient,
        IOptions<ExtendClientOptions> extendOptions,
        ILogger<ImportacaoService> logger)
    {
        _loteService = loteService;
        _documentoService = documentoService;
        _extendClient = extendClient;
        _extendOptions = extendOptions.Value;
        _logger = logger;
        _uploadRoot = Path.Combine("wwwroot", "uploads", "lotes");
    }

    /// <summary>
    /// Recebe o arquivo ZIP, salva em disco e cria o lote com status Pendente.
    /// </summary>
    public async Task<LoteDto> ReceberArquivoAsync(Stream stream, string nomeArquivo, long tamanho)
    {
        Directory.CreateDirectory(_uploadRoot);
        var nomeUnico = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Path.GetFileName(nomeArquivo)}";
        var caminho = Path.Combine(_uploadRoot, nomeUnico);

        await using (var fs = File.Create(caminho))
            await stream.CopyToAsync(fs);

        _logger.LogInformation("Arquivo ZIP recebido: {NomeArquivo} ({Tamanho} bytes) → {Caminho}", nomeArquivo, tamanho, caminho);

        var dto = new CriarLoteDto
        {
            NomeArquivo = nomeArquivo,
            CaminhoArquivo = caminho,
            TamanhoArquivo = tamanho
        };
        return await _loteService.CriarLoteAsync(dto);
    }

    /// <summary>
    /// Processa o lote:
    /// 1. Extrai o ZIP localmente
    /// 2. Classifica cada arquivo pelo nome (classificação preliminar para selecionar o extractor)
    /// 3. Envia cada arquivo individualmente para a Extend via POST /files/upload + POST /extract_runs
    /// 4. Salva o RunId retornado e atualiza status para AguardandoExtend
    /// 5. Atualiza status do lote para AguardandoExtend
    ///
    /// IMPORTANTE: As regras de auditoria (A-G) são executadas APENAS após o webhook da Extend.
    /// </summary>
    public async Task ProcessarLoteAsync(int loteId)
    {
        var lote = await _loteService.ObterPorIdAsync(loteId)
            ?? throw new InvalidOperationException($"Lote {loteId} não encontrado.");

        await _loteService.AtualizarStatusAsync(loteId, StatusLote.Processando);
        _logger.LogInformation("Iniciando processamento do lote {LoteId}: {NomeArquivo}", loteId, lote.NomeArquivo);

        try
        {
            // 1. Extrai o ZIP
            var pastaExtracao = Path.Combine(_uploadRoot, $"extracao_{loteId}");
            Directory.CreateDirectory(pastaExtracao);

            var caminhoZip = lote.CaminhoArquivo;
            if (!File.Exists(caminhoZip))
                throw new FileNotFoundException($"Arquivo ZIP não encontrado: {caminhoZip}");

            ZipFile.ExtractToDirectory(caminhoZip, pastaExtracao, overwriteFiles: true);
            _logger.LogInformation("ZIP extraído para: {Pasta}", pastaExtracao);

            var arquivos = Directory.GetFiles(pastaExtracao, "*.*", SearchOption.AllDirectories)
                .Where(f => ExtensoesSuportadas.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToList();

            if (!arquivos.Any())
            {
                await _loteService.AtualizarStatusAsync(loteId, StatusLote.Erro, "Nenhum arquivo suportado encontrado no ZIP.");
                return;
            }

            _logger.LogInformation("Lote {LoteId}: {Total} arquivo(s) encontrado(s) para envio à Extend.", loteId, arquivos.Count);

            int enviados = 0;
            int erros = 0;

            foreach (var arquivo in arquivos)
            {
                var nomeArquivo = Path.GetFileName(arquivo);

                // 2. Classifica tipo pelo nome do arquivo (classificação preliminar)
                var tipo = ClassificarTipoPorNome(nomeArquivo);

                // 3. Cria o registro do documento no banco
                var doc = await _documentoService.CriarAsync(loteId, nomeArquivo, arquivo);
                await _documentoService.AtualizarTipoAsync(doc.Id, tipo);

                // 4. Seleciona o extractor correto conforme o tipo
                var extractorId = SelecionarExtractor(tipo);

                if (string.IsNullOrWhiteSpace(extractorId))
                {
                    _logger.LogWarning("Lote {LoteId}: arquivo '{Nome}' com tipo '{Tipo}' não possui extractor configurado. Ignorando.", loteId, nomeArquivo, tipo);
                    await _documentoService.AtualizarStatusAsync(doc.Id, StatusDocumento.Erro, "Extractor não configurado para este tipo de documento.");
                    erros++;
                    continue;
                }

                try
                {
                    // 5. Envia o arquivo para a Extend: POST /files/upload
                    await using var fileStream = File.OpenRead(arquivo);
                    var fileId = await _extendClient.UploadFileAsync(fileStream, nomeArquivo);

                    // 6. Inicia a extração assíncrona: POST /extract_runs
                    var runId = await _extendClient.IniciarExtracaoAsync(fileId, extractorId);

                    // 7. Salva o RunId e atualiza status para AguardandoExtend
                    await _documentoService.SalvarExtendRunIdAsync(doc.Id, fileId, runId, extractorId);
                    await _loteService.IncrementarEnviadosExtendAsync(loteId);

                    enviados++;
                    _logger.LogInformation("Lote {LoteId}: '{Nome}' enviado para Extend. FileId={FileId} RunId={RunId}", loteId, nomeArquivo, fileId, runId);
                }
                catch (ExtendApiException ex)
                {
                    _logger.LogError(ex, "Lote {LoteId}: falha ao enviar '{Nome}' para Extend.", loteId, nomeArquivo);
                    await _documentoService.AtualizarStatusAsync(doc.Id, StatusDocumento.Erro, ex.Message);
                    erros++;
                }
            }

            // 8. Atualiza status do lote
            if (enviados == 0 && erros > 0)
            {
                await _loteService.AtualizarStatusAsync(loteId, StatusLote.Erro,
                    $"Todos os {erros} arquivo(s) falharam no envio para a Extend.");
            }
            else
            {
                await _loteService.AtualizarStatusAsync(loteId, StatusLote.AguardandoExtend);
                _logger.LogInformation("Lote {LoteId}: {Enviados} arquivo(s) enviados para Extend. Aguardando webhooks.", loteId, enviados);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar lote {LoteId}", loteId);
            await _loteService.AtualizarStatusAsync(loteId, StatusLote.Erro, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Classificação preliminar pelo nome do arquivo para selecionar o extractor correto.
    /// A classificação definitiva é feita com base nos dados extraídos pela Extend.
    /// </summary>
    private static TipoDocumento ClassificarTipoPorNome(string nomeArquivo)
    {
        var nome = nomeArquivo.ToLowerInvariant();
        if (nome.Contains("guia") || nome.Contains("sadt") || nome.Contains("sp_") || nome.Contains("spsa"))
            return TipoDocumento.GuiaSPSADT;
        if (nome.Contains("pedido") || nome.Contains("solicitacao") || nome.Contains("prescricao"))
            return TipoDocumento.PedidoMedico;
        if (nome.Contains("laudo"))
            return TipoDocumento.Laudo;
        if (nome.Contains("receita"))
            return TipoDocumento.Receita;
        return TipoDocumento.Desconhecido;
    }

    private string SelecionarExtractor(TipoDocumento tipo) => tipo switch
    {
        TipoDocumento.GuiaSPSADT   => _extendOptions.ExtractorIdGuiaSPSADT,
        TipoDocumento.PedidoMedico => _extendOptions.ExtractorIdPedidoMedico,
        _                          => string.Empty
    };
}
