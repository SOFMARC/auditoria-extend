using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AuditoriaExtend.Application.Configuration;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;

namespace AuditoriaExtend.Application.Services;

/// <summary>
/// Serviço responsável por executar a análise antifraude de um lote via LLM (GPT-4).
/// Só deve ser chamado para lotes sem divergências pendentes de revisão humana.
/// </summary>
public class FraudeAnaliseService : IFraudeAnaliseService
{
    private readonly IRepository<ResultadoFraudeAnalise> _repoResultado;
    private readonly IRepository<Documento> _repoDoc;
    private readonly IRepository<DivergenciaAuditoria> _repoDiv;
    private readonly IRepository<Lote> _repoLote;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AuditoriaOptions _opts;
    private readonly ILogger<FraudeAnaliseService> _logger;

    // System prompt completo conforme especificação do usuário
    private const string SystemPrompt = """
Você é um auditor inteligente especializado em conferência antifraude de pedido médico + guias TISS extraídas por OCR e já validadas pelo usuário.

Seu trabalho é auditar um LOTE de documentos, e não apenas uma guia isolada.

## OBJETIVO
Analisar:
1. 1 pedido médico OCRado e validado
2. 1 ou mais guias OCRadas e validadas que pertencem ao mesmo atendimento/lote
3. opcionalmente histórico de documentos relacionados

Você deve identificar:
- inconsistências entre pedido e guias
- divergências de quantidade, item, data, código e valor
- possível duplicidade
- possíveis cobranças indevidas
- possíveis indícios de fraude documental
- achados que exigem auditoria humana

IMPORTANTE:
- Não conclua "fraude" como fato.
- Use termos como: "indício", "inconsistência", "suspeita", "possível irregularidade".
- Sempre priorize rastreabilidade.
- Sempre explique a evidência.
- Não invente dados ausentes.

## REGRA DE NORMALIZAÇÃO DE CHAVES
Antes de auditar:
1. Trate camelCase e snake_case como equivalentes.
2. Escolha apenas um valor final por campo.
3. Se ambos existirem e forem iguais, mantenha só um valor lógico.
4. Se ambos existirem e forem diferentes, registre inconsistência de OCR/dado estruturado.
5. Nunca duplique contagens, itens ou totais por causa de campos repetidos em formatos diferentes.

## REGRA DE CONSOLIDAÇÃO DO LOTE
Você deve consolidar o lote de guias antes de comparar com o pedido.
Considere que várias guias podem pertencer ao mesmo pedido quando houver forte compatibilidade em campos como:
- paciente, número da carteira, data da solicitação, médico/profissional solicitante, CRM/número do conselho, contratado solicitante, executante, contexto textual semelhante

Ao consolidar: una todos os itensRealizados, una todos os itensSolicitados, preserve a origem de cada item (número da guia), some valores por item e no total do lote.

## EQUIVALÊNCIAS SEMÂNTICAS OBRIGATÓRIAS
- GLICEMIA_JEJUM ≈ GLICOSE
- TGO ≈ TGO_AST | TGP ≈ TGP_ALT | GAMA_GT ≈ GGT | TSH_ULTRA_SENSIVEL ≈ TSH
- VITAMINA_D_25OH ≈ 25OH_VITAMINA_D
- MAMOGRAFIA_BILATERAL_DIGITAL ≈ MAMOGRAFIA_DIGITAL quando a lateralidade/digitalização estiver coerente
- UROCULTURA ≈ CULTURA_DE_URINA_JATO_MEDIO
- ANTIBIOGRAMA_URINA ≈ ANTIBIOGRAMA_DE_URINA_JATO_MEDIO
- COLESTEROL_TOTAL_FRACOES pode ser atendido por COLESTEROL_TOTAL + HDL_COLESTEROL + LDL_COLESTEROL + VLDL_COLESTEROL
- UROCULTURA_ANTIBIOGRAMA pode ser atendido por UROCULTURA + ANTIBIOGRAMA_URINA

## REGRAS DE COBERTURA
Para cada item do pedido: procurar correspondência exata por código, depois por descrição normalizada, depois por equivalência semântica, depois por conjunto de itens.
Para cada item realizado: verificar se corresponde ao pedido; se não, marcar possível cobrança sem lastro.

## REGRAS DE VALOR
1. Validar se soma dos valor_total dos itensRealizados bate com totalProcedimentos.
2. Validar se totalProcedimentos + totalMateriais + totalMedicamentos + totalTaxasAlugueis + totalGasesMedicinais + totalOpme bate com totalGeral.
3. Item com valor zero: não classifique como fraude automaticamente; trate como achado de conferência financeira.

## REGRAS DE POSSÍVEL FRAUDE
F1: Item realizado sem cobertura no pedido
F2: Parte relevante do pedido não aparece em nenhuma guia
F3: Mesmo procedimento repetido em mais de uma guia sem justificativa
F4: Quantidade consolidada realizada excede quantidade prescrita/autorizada
F5: Divergência relevante de código + descrição (não explicável por OCR)
F6: Somas de itens não batem com totais
F7: Item zerado em contexto de inconsistência adicional
F8: Procedimento simples quebrado em vários itens sem justificativa técnica
F9: Paciente, médico, CRM, data ou estrutura incompatíveis entre pedido e guias
F10: 3 ou mais achados relevantes no mesmo lote

## REGRAS PARA NÃO GERAR FALSO POSITIVO
NÃO marcar fraude quando: item agregado desmembrado nas guias, pequena variação textual compatível, código com pontuação OCRada incorretamente, ausência de assinatura em documento com baixa legibilidade, item zerado complementar/acessório, múltiplas guias que juntas cobrem o pedido, sinônimos clínicos compatíveis.

## CÁLCULO DE RISCO
Score 0-100: +5 a +15 inconsistências leves, +15 a +30 itens sem cobertura, +20 a +35 duplicidade provável, +10 a +20 divergência financeira, +5 a +15 falha documental.
Reduza score quando: baixa legibilidade, forte equivalência semântica, lote consolidado cobre adequadamente o pedido.
Mapeamento: 0-19 baixo, 20-39 moderado, 40-69 alto, 70-100 crítico.

## SAÍDA OBRIGATÓRIA
Responda apenas em JSON válido, sem markdown, sem comentários e sem texto adicional.

Estrutura obrigatória:
{
  "status_auditoria": "aprovado|aprovado_com_ressalvas|reter_para_analise_manual|bloquear_para_auditoria",
  "score_risco": 0,
  "nivel_risco": "baixo|moderado|alto|critico",
  "resumo": "",
  "lote": {
    "quantidade_guias": 0,
    "guias_analisadas": [],
    "paciente": "",
    "numero_carteira": "",
    "data_pedido": "",
    "data_solicitacao_lote": "",
    "medico": "",
    "crm_ou_conselho": ""
  },
  "cobertura_pedido": {
    "itens_cobertos": [],
    "itens_cobertura_parcial": [],
    "itens_nao_cobertos": [],
    "itens_realizados_sem_lastro": []
  },
  "validacoes_financeiras": {
    "soma_itens_confere_total_procedimentos": null,
    "composicao_total_geral_confere": null,
    "achados_financeiros": []
  },
  "validacoes_documentais": {
    "paciente_coerente": null,
    "medico_coerente": null,
    "assinatura_medico_presente": null,
    "carimbo_medico_presente": null,
    "assinatura_beneficiario_presente_em_todas_guias": null,
    "legibilidade_reduz_confianca": null
  },
  "achados": [
    {
      "regra": "",
      "titulo": "",
      "severidade": "baixa|media|alta|critica",
      "confianca": 0.0,
      "evidencia": "",
      "guia_relacionada": "",
      "item_relacionado": "",
      "acao_sugerida": ""
    }
  ],
  "recomendacao_final": ""
}

## REGRAS FINAIS DE DECISÃO
1. Se o lote consolidado cobrir adequadamente o pedido, e as divergências forem apenas semânticas ou de OCR, não reter por fraude.
2. Se houver pequena divergência documental com forte compatibilidade clínica e estrutural, classificar como aprovado_com_ressalvas.
3. Se houver itens não cobertos, duplicidade, divergência financeira ou conflito relevante de identidade, classificar como reter_para_analise_manual.
4. Se houver múltiplos indícios fortes combinados, classificar como bloquear_para_auditoria.
5. Sempre explicar objetivamente o motivo.

Agora analise os dados recebidos seguindo exatamente essas instruções.
""";

    public FraudeAnaliseService(
        IRepository<ResultadoFraudeAnalise> repoResultado,
        IRepository<Documento> repoDoc,
        IRepository<DivergenciaAuditoria> repoDiv,
        IRepository<Lote> repoLote,
        IHttpClientFactory httpClientFactory,
        IOptions<AuditoriaOptions> opts,
        ILogger<FraudeAnaliseService> logger)
    {
        _repoResultado = repoResultado;
        _repoDoc = repoDoc;
        _repoDiv = repoDiv;
        _repoLote = repoLote;
        _httpClientFactory = httpClientFactory;
        _opts = opts.Value;
        _logger = logger;
    }

    /// <summary>
    /// Verifica se o lote está pronto para análise antifraude:
    /// - Status Concluido
    /// - Sem divergências com status Pendente ou EmRevisao
    /// - Sem ResultadoFraudeAnalise já existente (Concluido ou Processando)
    /// </summary>
    public async Task<bool> LoteElegivelParaAnaliseAsync(int loteId)
    {
        var lote = await _repoLote.GetByIdAsync(loteId);
        if (lote == null || lote.Status != StatusLote.Concluido) return false;

        var divs = (await _repoDiv.GetAllAsync())
            .Where(d => d.DocumentoId > 0)
            .ToList();

        // Busca documentos do lote para filtrar divergências
        var docIds = (await _repoDoc.GetAllAsync())
            .Where(d => d.LoteId == loteId)
            .Select(d => d.Id)
            .ToHashSet();

        var temPendente = divs.Any(d => docIds.Contains(d.DocumentoId)
            && (d.Status == StatusDivergencia.Pendente || d.Status == StatusDivergencia.EmRevisao));

        if (temPendente) return false;

        // Verifica se já existe análise em andamento ou concluída
        var jaExiste = (await _repoResultado.GetAllAsync())
            .Any(r => r.LoteId == loteId
                && (r.Status == StatusFraudeAnalise.Concluido || r.Status == StatusFraudeAnalise.Processando));

        return !jaExiste;
    }

    /// <summary>
    /// Executa a análise antifraude do lote via LLM e persiste o resultado.
    /// </summary>
    public async Task<ResultadoFraudeAnalise> AnalisarLoteAsync(int loteId, CancellationToken ct = default)
    {
        _logger.LogInformation("FraudeAnalise: iniciando análise do lote {LoteId}", loteId);

        // Cria registro de controle com status Processando
        var resultado = new ResultadoFraudeAnalise
        {
            LoteId = loteId,
            Status = StatusFraudeAnalise.Processando,
            DataInicio = DateTime.UtcNow,
            DataCriacao = DateTime.UtcNow
        };
        await _repoResultado.AddAsync(resultado);

        try
        {
            // Monta o payload para o LLM
            var payload = await MontarPayloadLoteAsync(loteId);
            var userMessage = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });

            // Chama o LLM
            var respostaJson = await ChamarLlmAsync(userMessage, ct);

            // Faz parse do resultado
            using var doc = JsonDocument.Parse(respostaJson);
            var root = doc.RootElement;

            resultado.ResultadoJson = respostaJson;
            resultado.StatusAuditoria = root.TryGetProperty("status_auditoria", out var sa) ? sa.GetString() : null;
            resultado.ScoreRisco = root.TryGetProperty("score_risco", out var sr) && sr.TryGetInt32(out var srInt) ? srInt : null;
            resultado.NivelRisco = root.TryGetProperty("nivel_risco", out var nr) ? nr.GetString() : null;
            resultado.Resumo = root.TryGetProperty("resumo", out var res) ? res.GetString() : null;
            resultado.RecomendacaoFinal = root.TryGetProperty("recomendacao_final", out var rf) ? rf.GetString() : null;
            resultado.QuantidadeAchados = root.TryGetProperty("achados", out var achados) && achados.ValueKind == JsonValueKind.Array
                ? achados.GetArrayLength() : 0;
            resultado.Status = StatusFraudeAnalise.Concluido;
            resultado.DataFim = DateTime.UtcNow;
            resultado.DataAtualizacao = DateTime.UtcNow;

            await _repoResultado.UpdateAsync(resultado);
            _logger.LogInformation("FraudeAnalise: lote {LoteId} analisado — status={Status} score={Score}",
                loteId, resultado.StatusAuditoria, resultado.ScoreRisco);
        }
        catch (Exception ex)
        {
            resultado.Status = StatusFraudeAnalise.Erro;
            resultado.MensagemErro = ex.Message;
            resultado.DataFim = DateTime.UtcNow;
            resultado.DataAtualizacao = DateTime.UtcNow;
            await _repoResultado.UpdateAsync(resultado);
            _logger.LogError(ex, "FraudeAnalise: erro ao analisar lote {LoteId}", loteId);
        }

        return resultado;
    }

    /// <summary>
    /// Monta o payload JSON com pedido médico e guias do lote para enviar ao LLM.
    /// </summary>
    private async Task<object> MontarPayloadLoteAsync(int loteId)
    {
        var documentos = (await _repoDoc.GetAllAsync())
            .Where(d => d.LoteId == loteId && d.Status == StatusDocumento.Processado)
            .ToList();

        var pedidos = new List<object>();
        var guias = new List<object>();

        foreach (var doc in documentos)
        {
            if (string.IsNullOrWhiteSpace(doc.DadosExtraidos) || doc.DadosExtraidos == "{}") continue;

            object? dadosObj = null;
            try { dadosObj = JsonSerializer.Deserialize<object>(doc.DadosExtraidos); } catch { continue; }
            if (dadosObj == null) continue;

            if (doc.TipoDocumento == TipoDocumento.PedidoMedico)
                pedidos.Add(new { arquivo = doc.NomeArquivo, dados = dadosObj });
            else if (doc.TipoDocumento == TipoDocumento.GuiaSPSADT)
                guias.Add(new { arquivo = doc.NomeArquivo, dados = dadosObj });
        }

        return new
        {
            lote_id = loteId,
            pedido_medico = pedidos.Count == 1 ? (object)pedidos[0] : pedidos,
            guias = guias
        };
    }

    /// <summary>
    /// Chama a API OpenAI-compatible com o system prompt e o payload do lote.
    /// </summary>
    private async Task<string> ChamarLlmAsync(string userMessage, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                     ?? throw new InvalidOperationException("OPENAI_API_KEY não configurada.");

        var client = _httpClientFactory.CreateClient("openai");
        client.BaseAddress = new Uri("https://api.openai.com/v1/");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var requestBody = new
        {
            model = "gpt-4.1-mini",
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userMessage }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("chat/completions", content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"LLM API error {response.StatusCode}: {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        var messageContent = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        return messageContent;
    }

    /// <summary>
    /// Retorna o resultado de análise mais recente para um lote.
    /// </summary>
    public async Task<ResultadoFraudeAnalise?> ObterResultadoAsync(int loteId)
    {
        return (await _repoResultado.GetAllAsync())
            .Where(r => r.LoteId == loteId)
            .OrderByDescending(r => r.DataCriacao)
            .FirstOrDefault();
    }
}
