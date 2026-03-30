using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;

namespace AuditoriaExtend.Application.Services;

/// <summary>
/// Processa o retorno do webhook da Extend após a extração de um documento.
/// Fluxo: recebe payload JSON → localiza documento pelo RunId → persiste dados extraídos
/// → executa todas as regras de auditoria (A-G) → atualiza status do lote.
/// </summary>
public class WebhookProcessorService : IWebhookProcessorService
{
    private readonly IRepository<Documento> _repoDoc;
    private readonly IRepository<Lote> _repoLote;
    private readonly IDocumentoService _documentoService;
    private readonly ILoteService _loteService;
    private readonly IAuditoriaRegraService _auditoriaService;
    private readonly ILogger<WebhookProcessorService> _logger;

    public WebhookProcessorService(
        IRepository<Documento> repoDoc,
        IRepository<Lote> repoLote,
        IDocumentoService documentoService,
        ILoteService loteService,
        IAuditoriaRegraService auditoriaService,
        ILogger<WebhookProcessorService> logger)
    {
        _repoDoc = repoDoc;
        _repoLote = repoLote;
        _documentoService = documentoService;
        _loteService = loteService;
        _auditoriaService = auditoriaService;
        _logger = logger;
    }

    public async Task ProcessarRetornoExtendAsync(string payloadJson)
    {
        _logger.LogInformation("Webhook Processor: payload bruto recebido");

        var payload = JObject.Parse(payloadJson);
        try
        {
            payload = JObject.Parse(payloadJson);
            _logger.LogInformation("Webhook : payloadJson={payload}", payload);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook: payload JSON inválido");
            return;
        }
        var eventType = payload["eventType"]?.ToString();
        var eventId = payload["eventId"]?.ToString();
        var runId = payload["payload"]?["id"]?.ToString();
        var status = payload["payload"]?["status"]?.ToString();

        _logger.LogInformation("Webhook Processor: eventType={EventType} eventId={EventId} runId={RunId} status={Status}",
            eventType, eventId, runId, status);

        // Só processa o evento de extração concluída
        if (eventType != "extract_run.processed" && eventType != "extract_run.failed")
        {
            _logger.LogDebug("Webhook: evento ignorado '{Event}'", eventType);
            return;
        }

        var data = payload["data"];

        if (string.IsNullOrEmpty(runId))
        {
            _logger.LogWarning("Webhook: RunId ausente no payload");
            return;
        }

        // Localiza o documento pelo RunId
        var todos = await _repoDoc.GetAllAsync();
        var documento = todos.FirstOrDefault(d => d.ExtendRunId == runId);

        if (documento == null)
        {
            _logger.LogWarning("Webhook: documento não encontrado para RunId={RunId}", runId);
            return;
        }

        if (status == "failed")
        {
            var erro = data?["error"]?.ToString() ?? "Erro desconhecido na extração Extend";
            _logger.LogError("Webhook: extração falhou para RunId={RunId}. Erro={Erro}", runId, erro);
            await _documentoService.AtualizarStatusAsync(documento.Id, StatusDocumento.Erro, erro);
            await VerificarConclusaoLoteAsync(documento.LoteId);
            return;
        }

        // Extrai os dados do output
        var output = data?["output"];
        var fieldsJson = output?["fields"]?.ToString(Formatting.None) ?? "{}";
        var ocrConfidence = output?["ocrConfidence"]?.Value<double>() ?? 0.0;
        var reviewAgentScore = output?["reviewAgentScore"]?.Value<int?>();
        var extractorId = documento.ExtractorId ?? string.Empty;

        _logger.LogInformation(
            "Webhook: processando RunId={RunId} DocId={DocId} OcrConfidence={Ocr} ReviewScore={Score}",
            runId, documento.Id, ocrConfidence, reviewAgentScore);

        // Persiste os dados extraídos
        await _documentoService.SalvarDadosExtracaoAsync(
            documento.Id, fieldsJson, ocrConfidence, extractorId, reviewAgentScore);
        _logger.LogInformation("SalvarDadosExtracaoAsync");

        // Executa todas as regras de auditoria (A, B, C, D, E, F, G)
        await _auditoriaService.AuditarDocumentoAsync(documento.Id);
        _logger.LogInformation("AuditarDocumentoAsync");

        await _loteService.IncrementarProcessadosAsync(documento.LoteId);
        _logger.LogInformation("IncrementarProcessadosAsync");

        await VerificarConclusaoLoteAsync(documento.LoteId);
        _logger.LogInformation("VerificarConclusaoLoteAsync");
    }

    /// <summary>
    /// Verifica se todos os documentos do lote foram processados.
    /// Se sim, executa as regras de auditoria cross-documento (duplicidade, ausência documental)
    /// e atualiza o status do lote para Concluido.
    /// </summary>
    private async Task VerificarConclusaoLoteAsync(int loteId)
    {
        var lote = await _repoLote.GetByIdAsync(loteId);
        if (lote == null) return;

        var docs = await _repoDoc.GetAllAsync();
        var docsLote = docs.Where(d => d.LoteId == loteId).ToList();

        var totalPendentes = docsLote.Count(d =>
            d.Status == StatusDocumento.AguardandoExtend ||
            d.Status == StatusDocumento.EnviadoExtend ||
            d.Status == StatusDocumento.Pendente);

        if (totalPendentes > 0)
        {
            _logger.LogDebug("Lote {LoteId}: ainda aguardando {Pendentes} documentos da Extend", loteId, totalPendentes);
            return;
        }

        _logger.LogInformation("Lote {LoteId}: todos os documentos processados. Executando regras cross-documento.", loteId);

        // Regra E: Duplicidade entre documentos do lote
        await _auditoriaService.DetectarDuplicidadesAsync(loteId);

        // Regra F: Ausência documental (guia sem pedido / pedido sem guia)
        await _auditoriaService.DetectarAusenciaDocumentalAsync(loteId);

        // Regra G: Calcular score de risco por atendimento
        await _auditoriaService.CalcularScoreRiscoAsync(loteId);

        await _loteService.AtualizarStatusAsync(loteId, StatusLote.Concluido);
        _logger.LogInformation("Lote {LoteId}: concluído com sucesso.", loteId);
    }
}
