using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuditoriaExtend.Application.Services;

/// <summary>
/// Processa o retorno do webhook da Extend após a extração de um documento.
///
/// Fluxo completo:
///   1. Recebe o payload JSON bruto do WebhookController
///   2. Desserializa usando a estrutura REAL da Extend:
///      { "eventId": "...", "eventType": "extract_run.processed",
///        "payload": { "id": "exr_...", "status": "PROCESSED",
///                     "output": { "value": { "numero_carteira": "...", "itens_realizados": [...] },
///                                 "metadata": { "numero_carteira": { "confidence": 0.98, ... } } } } }
///   3. Localiza o documento pelo ExtendRunId
///   4. Normaliza value + metadata para o formato interno (snake_case -> camelCase)
///   5. Persiste dados extraídos (DocumentosExtracao + flags no Documento)
///   6. Cria ou atualiza AtendimentoAgrupado por (LoteId + numero_carteira)
///   7. Vincula o Documento ao AtendimentoAgrupado
///   8. Executa regras de auditoria por documento (C e D)
///   9. Quando todos os documentos do lote estiverem processados, executa E, F e G (cross-documento)
/// </summary>
public class WebhookProcessorService : IWebhookProcessorService
{
    private readonly IRepository<Documento> _repoDoc;
    private readonly IRepository<Lote> _repoLote;
    private readonly IRepository<AtendimentoAgrupado> _repoAtendimento;
    private readonly IDocumentoService _documentoService;
    private readonly ILoteService _loteService;
    private readonly IAuditoriaRegraService _auditoriaService;
    private readonly ILogger<WebhookProcessorService> _logger;

    // Mapeamento de campos snake_case (Extend) -> camelCase (dominio interno)
    private static readonly Dictionary<string, string> MapaCampos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["numero_carteira"]         = "numeroCarteira",
        ["nome_paciente"]           = "nomePaciente",
        ["nome_beneficiario"]       = "nomePaciente",
        ["cpf_paciente"]            = "cpfPaciente",
        ["data_nascimento"]         = "dataNascimento",
        ["crm"]                     = "crm",
        ["crm_medico"]              = "crm",
        ["nome_medico"]             = "nomeMedico",
        ["nome_solicitante"]        = "nomeMedico",
        ["especialidade"]           = "especialidade",
        ["codigo_cnes"]             = "codigoCnes",
        ["cnes"]                    = "codigoCnes",
        ["numero_guia"]             = "numeroGuia",
        ["numero_guia_prestador"]   = "numeroGuia",
        ["numero_pedido"]           = "numeroPedido",
        ["numero_autorizacao"]      = "numeroAutorizacao",
        ["data_solicitacao"]        = "dataSolicitacao",
        ["data_atendimento"]        = "dataAtendimento",
        ["data_realizacao"]         = "dataAtendimento",
        ["itens_solicitados"]       = "itensSolicitados",
        ["itens_realizados"]        = "itensRealizados",
        ["procedimentos"]           = "itensSolicitados",
        ["codigo_procedimento"]     = "codigoProcedimento",
        ["procedimento"]            = "procedimento",
        ["quantidade_solicitada"]   = "quantidadeSolicitada",
        ["quantidade_realizada"]    = "quantidadeRealizada",
        ["quantidades_solicitadas"] = "quantidadesSolicitadas",
        ["quantidades_realizadas"]  = "quantidadesRealizadas",
        ["total_opme"]              = "totalOpme",
        ["total_procedimentos"]     = "totalProcedimentos",
        ["total_geral"]             = "totalGeral",
        ["valor_unitario"]          = "valorUnitario",
        ["valor_total"]             = "valorTotal",
        ["cid"]                     = "cid",
        ["indicacao_clinica"]       = "indicacaoClinica",
        ["diagnostico"]             = "indicacaoClinica",
    };

    public WebhookProcessorService(
        IRepository<Documento> repoDoc,
        IRepository<Lote> repoLote,
        IRepository<AtendimentoAgrupado> repoAtendimento,
        IDocumentoService documentoService,
        ILoteService loteService,
        IAuditoriaRegraService auditoriaService,
        ILogger<WebhookProcessorService> logger)
    {
        _repoDoc = repoDoc;
        _repoLote = repoLote;
        _repoAtendimento = repoAtendimento;
        _documentoService = documentoService;
        _loteService = loteService;
        _auditoriaService = auditoriaService;
        _logger = logger;
    }

    public async Task ProcessarRetornoExtendAsync(string payloadJson)
    {
        JObject root;
        try { root = JObject.Parse(payloadJson); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook Extend: payload JSON invalido");
            return;
        }

        var eventType = root["eventType"]?.ToString();
        var eventId   = root["eventId"]?.ToString();

        _logger.LogInformation(
            "Webhook Processor: eventType=\"{EventType}\" eventId=\"{EventId}\"",
            eventType, eventId);

        if (eventType != "extract_run.processed" && eventType != "extract_run.failed")
        {
            _logger.LogDebug("Webhook Extend: evento ignorado '{EventType}'", eventType);
            return;
        }

        var data   = root["payload"];
        var runId  = data?["id"]?.ToString();
        var status = data?["status"]?.ToString()?.ToUpperInvariant();

        if (string.IsNullOrEmpty(runId))
        {
            _logger.LogWarning("Webhook Extend: RunId ausente no payload");
            return;
        }

        _logger.LogInformation(
            "Webhook Processor: runId=\"{RunId}\" status=\"{Status}\"",
            runId, status);

        var todos = await _repoDoc.GetAllAsync();
        var documento = todos.FirstOrDefault(d => d.ExtendRunId == runId);

        if (documento == null)
        {
            _logger.LogWarning("Webhook Extend: documento nao encontrado para RunId={RunId}", runId);
            return;
        }

        if (status == "FAILED")
        {
            var erro = data?["error"]?.ToString() ?? "Erro desconhecido na extracao Extend";
            _logger.LogError("Webhook Extend: extracao falhou. RunId={RunId} Erro={Erro}", runId, erro);
            await _documentoService.AtualizarStatusAsync(documento.Id, StatusDocumento.Erro, erro);
            await VerificarConclusaoLoteAsync(documento.LoteId);
            return;
        }

        var output   = data?["output"];
        var valueObj = output?["value"] as JObject;
        var metaObj  = output?["metadata"] as JObject;

        if (valueObj == null)
            _logger.LogWarning("Webhook Extend: RunId={RunId} output.value ausente", runId);

        var dadosNormalizados = NormalizarCampos(valueObj, metaObj);
        var dadosJson = dadosNormalizados.ToString(Formatting.None);

        double ocrConfidenceGeral = 0.0;
        int? reviewScoreGeral = null;

        if (metaObj != null)
        {
            ocrConfidenceGeral = CalcularConfiancaMedia(metaObj);
            reviewScoreGeral   = ObterReviewScoreMinimo(metaObj);
        }

        if (ocrConfidenceGeral == 0.0)
            ocrConfidenceGeral = output?["ocrConfidence"]?.Value<double?>() ?? 0.0;
        if (!reviewScoreGeral.HasValue)
            reviewScoreGeral = output?["reviewAgentScore"]?.Value<int?>();

        _logger.LogInformation(
            "Webhook Extend: DocId={DocId} RunId={RunId} OcrConf={Ocr:F2} ReviewScore={Score} Campos={Campos}",
            documento.Id, runId, ocrConfidenceGeral, reviewScoreGeral, dadosNormalizados.Count);

        await _documentoService.SalvarDadosExtracaoAsync(
            documento.Id, dadosJson, ocrConfidenceGeral,
            documento.ExtractorId ?? string.Empty, reviewScoreGeral);

        // CRITICO: Cria ou atualiza AtendimentoAgrupado antes das regras cross-documento
        await AgruparDocumentoAsync(documento, dadosNormalizados);

        await _auditoriaService.AuditarDocumentoAsync(documento.Id);

        await _loteService.IncrementarProcessadosAsync(documento.LoteId);
        await VerificarConclusaoLoteAsync(documento.LoteId);
    }

    private async Task AgruparDocumentoAsync(Documento documento, JObject dadosNormalizados)
    {
        var numeroCarteira = ExtrairValorCampo(dadosNormalizados, "numeroCarteira")
                          ?? ExtrairValorCampo(dadosNormalizados, "cpfPaciente")
                          ?? ExtrairValorCampo(dadosNormalizados, "nomePaciente");

        var nomePaciente = ExtrairValorCampo(dadosNormalizados, "nomePaciente");
        var nomeMedico   = ExtrairValorCampo(dadosNormalizados, "nomeMedico");
        var crmMedico    = ExtrairValorCampo(dadosNormalizados, "crm");
        var numeroGuia   = ExtrairValorCampo(dadosNormalizados, "numeroGuia");
        var numeroPedido = ExtrairValorCampo(dadosNormalizados, "numeroPedido");
        var dataAtendStr = ExtrairValorCampo(dadosNormalizados, "dataAtendimento")
                        ?? ExtrairValorCampo(dadosNormalizados, "dataSolicitacao");

        DateTime? dataAtendimento = null;
        if (!string.IsNullOrWhiteSpace(dataAtendStr) && DateTime.TryParse(dataAtendStr, out var dtParsed))
            dataAtendimento = dtParsed;

        var chaveAgrupamento = string.IsNullOrWhiteSpace(numeroCarteira)
            ? $"DOC_{documento.Id}"
            : numeroCarteira.Trim();

        _logger.LogInformation(
            "Webhook Extend: AgrupandoDoc={DocId} LoteId={LoteId} Chave=\"{Chave}\"",
            documento.Id, documento.LoteId, chaveAgrupamento);

        var todosAtendimentos = await _repoAtendimento.GetAllAsync();
        var atendimento = todosAtendimentos.FirstOrDefault(a =>
            a.LoteId == documento.LoteId &&
            string.Equals(a.NumeroCarteira, chaveAgrupamento, StringComparison.OrdinalIgnoreCase));

        if (atendimento == null)
        {
            atendimento = new AtendimentoAgrupado
            {
                LoteId               = documento.LoteId,
                NumeroCarteira       = chaveAgrupamento,
                NomePaciente         = nomePaciente,
                NomeMedico           = nomeMedico,
                CrmMedico            = crmMedico,
                NumeroGuia           = numeroGuia,
                NumeroPedido         = numeroPedido,
                DataAtendimento      = dataAtendimento,
                QuantidadeDocumentos = 1,
                DataCriacao          = DateTime.UtcNow,
                DataAtualizacao      = DateTime.UtcNow,
            };
            await _repoAtendimento.AddAsync(atendimento);
            await _repoAtendimento.SaveChangesAsync();

            _logger.LogInformation(
                "Webhook Extend: AtendimentoAgrupado CRIADO Id={AtendId} LoteId={LoteId} Carteira=\"{Carteira}\"",
                atendimento.Id, documento.LoteId, chaveAgrupamento);
        }
        else
        {
            atendimento.QuantidadeDocumentos++;
            if (string.IsNullOrWhiteSpace(atendimento.NomePaciente) && !string.IsNullOrWhiteSpace(nomePaciente))
                atendimento.NomePaciente = nomePaciente;
            if (string.IsNullOrWhiteSpace(atendimento.NomeMedico) && !string.IsNullOrWhiteSpace(nomeMedico))
                atendimento.NomeMedico = nomeMedico;
            if (string.IsNullOrWhiteSpace(atendimento.CrmMedico) && !string.IsNullOrWhiteSpace(crmMedico))
                atendimento.CrmMedico = crmMedico;
            if (string.IsNullOrWhiteSpace(atendimento.NumeroGuia) && !string.IsNullOrWhiteSpace(numeroGuia))
                atendimento.NumeroGuia = numeroGuia;
            if (string.IsNullOrWhiteSpace(atendimento.NumeroPedido) && !string.IsNullOrWhiteSpace(numeroPedido))
                atendimento.NumeroPedido = numeroPedido;
            if (!atendimento.DataAtendimento.HasValue && dataAtendimento.HasValue)
                atendimento.DataAtendimento = dataAtendimento;
            atendimento.DataAtualizacao = DateTime.UtcNow;
            await _repoAtendimento.UpdateAsync(atendimento);
            await _repoAtendimento.SaveChangesAsync();

            _logger.LogInformation(
                "Webhook Extend: AtendimentoAgrupado ATUALIZADO Id={AtendId} LoteId={LoteId} Docs={Docs}",
                atendimento.Id, documento.LoteId, atendimento.QuantidadeDocumentos);
        }

        await _documentoService.VincularAtendimentoAsync(documento.Id, atendimento.Id);
    }

    private static JObject NormalizarCampos(JObject? valueObj, JObject? metaObj)
    {
        var resultado = new JObject();
        if (valueObj == null) return resultado;

        foreach (var prop in valueObj.Properties())
        {
            var nomeCampoOriginal = prop.Name;
            var nomeCampoNorm = MapaCampos.TryGetValue(nomeCampoOriginal, out var mapped)
                ? mapped
                : SnakeToCamel(nomeCampoOriginal);

            var campoNorm = new JObject { ["value"] = prop.Value };

            JObject? meta = null;
            if (metaObj != null)
            {
                meta = metaObj[nomeCampoOriginal] as JObject
                    ?? metaObj[nomeCampoNorm] as JObject;
            }

            if (meta != null)
            {
                campoNorm["confidence"]       = meta["confidence"];
                campoNorm["reviewAgentScore"] = meta["reviewAgentScore"];
                campoNorm["citation"]         = meta["citation"];
                campoNorm["pageNumber"]       = meta["pageNumber"];
                campoNorm["isPrintedPage"]    = meta["isPrintedPage"];
            }

            resultado[nomeCampoNorm] = campoNorm;

            if (!string.Equals(nomeCampoOriginal, nomeCampoNorm, StringComparison.Ordinal))
                resultado[nomeCampoOriginal] = campoNorm;
        }

        return resultado;
    }

    private static string SnakeToCamel(string snake)
    {
        if (string.IsNullOrEmpty(snake)) return snake;
        var parts = snake.Split('_');
        if (parts.Length == 1) return snake;
        return parts[0] + string.Concat(parts.Skip(1).Select(p =>
            p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));
    }

    private static string? ExtrairValorCampo(JObject dadosNorm, string nomeCampo)
    {
        var token = dadosNorm[nomeCampo];
        if (token == null) return null;
        if (token is JObject obj)
        {
            var val = obj["value"];
            if (val == null || val.Type == JTokenType.Null) return null;
            if (val.Type == JTokenType.Array || val.Type == JTokenType.Object) return null;
            return val.ToString();
        }
        if (token.Type == JTokenType.Null) return null;
        if (token.Type == JTokenType.Array || token.Type == JTokenType.Object) return null;
        return token.ToString();
    }

    private static double CalcularConfiancaMedia(JObject metaObj)
    {
        var confidencias = metaObj.Properties()
            .Select(p => p.Value is JObject obj ? obj["confidence"]?.Value<double?>() : null)
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .ToList();
        return confidencias.Count > 0 ? confidencias.Average() : 0.0;
    }

    private static int? ObterReviewScoreMinimo(JObject metaObj)
    {
        var scores = metaObj.Properties()
            .Select(p => p.Value is JObject obj ? obj["reviewAgentScore"]?.Value<int?>() : null)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();
        return scores.Count > 0 ? scores.Min() : null;
    }

    private async Task VerificarConclusaoLoteAsync(int loteId)
    {
        var lote = await _repoLote.GetByIdAsync(loteId);
        if (lote == null) return;

        var docs = await _repoDoc.GetAllAsync();
        var docsLote = docs.Where(d => d.LoteId == loteId).ToList();

        var totalPendentes = docsLote.Count(d =>
            d.Status == StatusDocumento.AguardandoExtend ||
            d.Status == StatusDocumento.EnviadoExtend    ||
            d.Status == StatusDocumento.Pendente);

        if (totalPendentes > 0)
        {
            _logger.LogDebug(
                "Lote {LoteId}: ainda aguardando {Pendentes} documento(s) da Extend",
                loteId, totalPendentes);
            return;
        }

        _logger.LogInformation(
            "Lote {LoteId}: todos os documentos processados. Executando regras cross-documento (E, F, G).",
            loteId);

        await _auditoriaService.DetectarDuplicidadesAsync(loteId);
        await _auditoriaService.DetectarAusenciaDocumentalAsync(loteId);
        await _auditoriaService.CalcularScoreRiscoAsync(loteId);

        await _loteService.AtualizarStatusAsync(loteId, StatusLote.Concluido);

        _logger.LogInformation("Lote {LoteId}: concluido com sucesso.", loteId);
    }
}
