using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AuditoriaExtend.Application.Services;

/// <summary>
/// Implementa todas as regras de auditoria após o retorno da Extend.
/// A=IntraGuia | B=PedidoVsGuia | C=Confiança | D=OrigemSuspeita | E=Duplicidade | F=AusenciaDocumental | G=ScoreRisco
///
/// O JSON armazenado em Documento.DadosExtraidos está no formato normalizado pelo
/// WebhookProcessorService: apenas camelCase, cada campo como wrapper
/// { "value": X, "ocrConfidence": Y, "logprobsConfidence": Z, "citations": [...], ... }
/// </summary>
public class AuditoriaRegraService : IAuditoriaRegraService
{
    private readonly IRepository<Documento> _repoDoc;
    private readonly IRepository<Lote> _repoLote;
    private readonly IRepository<AtendimentoAgrupado> _repoAtendimento;
    private readonly IDivergenciaService _divService;
    private readonly IDocumentoService _documentoService;
    private readonly ILoteService _loteService;
    private readonly AuditoriaOptions _options;
    private readonly ILogger<AuditoriaRegraService> _logger;

    // Limiares de confiança (Regra C)
    private const double LimiarOcrCriticoObrigatorio = 0.30;
    private const double LimiarOcrImportanteAlerta   = 0.30;
    private const double LimiarReviewScoreBaixo      = 0.30;
    private const double LimiarReviewScoreAlerta      = 0.30;

    // Campos críticos — apenas camelCase (o JSON normalizado não contém snake_case).
    // Esses campos exigem revisão humana obrigatória quando a confiança é baixa.
    private static readonly HashSet<string> CamposCriticos = new(StringComparer.OrdinalIgnoreCase)
    {
        "nomePaciente", "crm", "numeroGuia", "numeroPedido",
        "dataSolicitacao", "dataAtendimento", "codigoProcedimento",
        "quantidadeSolicitada", "quantidadeRealizada", "totalGeral",
        "numeroCarteira", "itensSolicitados", "itensRealizados", "itensPedido",
    };

    // Campos importantes — geram alerta mas não revisão obrigatória.
    private static readonly HashSet<string> CamposImportantes = new(StringComparer.OrdinalIgnoreCase)
    {
        "nomeMedico", "especialidade", "cid", "indicacaoClinica",
        "valorUnitario", "valorTotal", "codigoCnes",
        "totalOpme", "totalProcedimentos",
    };

    public AuditoriaRegraService(
        IRepository<Documento> repoDoc,
        IRepository<Lote> repoLote,
        IRepository<AtendimentoAgrupado> repoAtendimento,
        IDivergenciaService divService,
        IDocumentoService documentoService,
        ILoteService loteService,
        IOptions<AuditoriaOptions> options,
        ILogger<AuditoriaRegraService> logger)
    {
        _repoDoc = repoDoc;
        _repoLote = repoLote;
        _repoAtendimento = repoAtendimento;
        _divService = divService;
        _documentoService = documentoService;
        _loteService = loteService;
        _options = options.Value;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGRAS C e D — Confiança e Origem Suspeita (por documento, pós-webhook)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<int> AuditarDocumentoAsync(int documentoId)
    {
        var doc = await _repoDoc.GetByIdAsync(documentoId);
        if (doc == null || string.IsNullOrWhiteSpace(doc.DadosExtraidos)) return 0;

        int divergencias = 0;
        bool revisaoNecessaria = false;
        bool origemSuspeita    = false;

        JObject? fields = null;
        try { fields = JObject.Parse(doc.DadosExtraidos); } catch { /* JSON inválido */ }

        if (fields != null)
        {
            // Obtém todos os metadados de uma vez via helper tipado.
            // IMPORTANTE: O JSON pode conter campos duplicados (snake_case + camelCase) em documentos
            // processados com versões antigas do WebhookProcessorService. Filtramos apenas camelCase.
            var metadados = ExtracaoJsonHelper.ObterMetadados(fields);

            // Detecta se o extractor suporta citações: se QUALQUER campo tiver citations preenchido,
            // então o extractor suporta e campos críticos sem citation são suspeitos.
            // Se NENHUM campo tiver citations, o extractor não suporta — não gerar C4.
            var extractorSuportaCitacoes = metadados.Any(m => m.Citations.Count > 0);

            foreach (var meta in metadados)
            {
                // Pula campos snake_case duplicados (legado): se existe versão camelCase do mesmo campo,
                // o snake_case é redundante e não deve gerar divergências extras.
                if (meta.NomeCampo.Contains('_')) continue;
                var nomeCampo = meta.NomeCampo;
                bool ehCritico    = CamposCriticos.Contains(nomeCampo);
                bool ehImportante = CamposImportantes.Contains(nomeCampo);

                // Usa reviewAgentScore (double) quando disponível; fallback para ocrConfidence
                var reviewScore = meta.ReviewAgentScore;
                var ocrConf     = meta.OcrConfidence;
                var value = ExtrairValorAuditoria(meta.Value);

                // C1: reviewAgentScore <= 3 em campo crítico → revisão obrigatória
                if (ehCritico && reviewScore.HasValue && reviewScore.Value <= LimiarReviewScoreBaixo)
                {
                    await _divService.CriarAsync(documentoId,
                        TipoDivergencia.CampoCriticoReviewScoreBaixo, SeveridadeDivergencia.Critica,
                        $"Campo crítico '{nomeCampo}' com reviewAgentScore={reviewScore:F1} (≤ {LimiarReviewScoreBaixo}) — revisão obrigatória.",
                        valorConfianca: ocrConf, campoAfetado: nomeCampo,
                        valorEncontrado: value, valorEsperado: $"reviewAgentScore > {LimiarReviewScoreBaixo}");
                    revisaoNecessaria = true;
                    divergencias++;
                }
                // C2: reviewAgentScore == 4 em campo crítico → alerta
                else if (ehCritico && reviewScore.HasValue && reviewScore.Value == LimiarReviewScoreAlerta)
                {
                    await _divService.CriarAsync(documentoId,
                        TipoDivergencia.CampoCriticoReviewScoreAlerta, SeveridadeDivergencia.Alta,
                        $"Campo crítico '{nomeCampo}' com reviewAgentScore={reviewScore:F1} — alerta de qualidade.",
                        valorConfianca: ocrConf, campoAfetado: nomeCampo, valorEncontrado: value);
                    divergencias++;
                }

                // C3: sem reviewAgentScore → usa ocrConfidence
                if (!reviewScore.HasValue)
                {
                    if (ehCritico && ocrConf.HasValue && ocrConf.Value < LimiarOcrCriticoObrigatorio)
                    {
                        await _divService.CriarAsync(documentoId,
                            TipoDivergencia.CampoCriticoOcrBaixo, SeveridadeDivergencia.Critica,
                            $"Campo crítico '{nomeCampo}' com ocrConfidence={ocrConf:P0} (< {LimiarOcrCriticoObrigatorio:P0}) — revisão obrigatória.",
                            valorConfianca: ocrConf, campoAfetado: nomeCampo,
                            valorEncontrado: value, valorEsperado: $">= {LimiarOcrCriticoObrigatorio:P0}");
                        revisaoNecessaria = true;
                        divergencias++;
                    }
                    else if (ehImportante && ocrConf.HasValue && ocrConf.Value < LimiarOcrImportanteAlerta)
                    {
                        await _divService.CriarAsync(documentoId,
                            TipoDivergencia.CampoImportanteOcrAlerta, SeveridadeDivergencia.Alta,
                            $"Campo importante '{nomeCampo}' com ocrConfidence={ocrConf:P0} (< {LimiarOcrImportanteAlerta:P0}) — alerta.",
                            valorConfianca: ocrConf, campoAfetado: nomeCampo, valorEncontrado: value);
                        divergencias++;
                    }
                }

                // C4: campo crítico sem citação (citations vazio ou ausente)
                // Só gera divergência se o extractor SUPORTA citações (pelo menos um campo tem citations).
                // Se nenhum campo tem citations, o extractor não foi configurado para isso — não é divergência.
                if (ehCritico && extractorSuportaCitacoes && meta.Citations.Count == 0 && !string.IsNullOrWhiteSpace(value))
                {
                    await _divService.CriarAsync(documentoId,
                        TipoDivergencia.CampoCriticoSemCitacao, SeveridadeDivergencia.Media,
                        $"Campo crítico '{nomeCampo}' sem citação no documento original.",
                        campoAfetado: nomeCampo, valorEncontrado: value);
                    divergencias++;
                }

                // D: Origem suspeita — item ancorado em página impressa posterior
                // (isPrintedPage indica que o campo veio de uma página adicionada após o documento original)
                // Nota: isPrintedPage não está no MetadadoCampo; verificamos diretamente no JObject
                var fieldObj = fields[nomeCampo] as JObject;
                var isPrintedPage = fieldObj?["isPrintedPage"]?.Value<bool?>() ?? false;
                var pageNumber    = fieldObj?["pageNumber"]?.Value<int?>();

                if (doc.TipoDocumento == TipoDocumento.PedidoMedico && isPrintedPage)
                {
                    if (_options.ModoAuditoria == ModoAuditoria.Estrito)
                    {
                        await _divService.CriarAsync(documentoId,
                            TipoDivergencia.OrigemSuspeita, SeveridadeDivergencia.Critica,
                            $"[MODO ESTRITO] Campo '{nomeCampo}' encontrado em página impressa posterior (pág. {pageNumber}) — item rejeitado.",
                            campoAfetado: nomeCampo, valorEncontrado: value);
                        revisaoNecessaria = true;
                        origemSuspeita    = true;
                        divergencias++;
                    }
                    else
                    {
                        await _divService.CriarAsync(documentoId,
                            TipoDivergencia.OrigemSuspeita, SeveridadeDivergencia.Alta,
                            $"[MODO ASSISTIDO] Campo '{nomeCampo}' encontrado em página impressa posterior (pág. {pageNumber}) — alerta de origem suspeita.",
                            campoAfetado: nomeCampo, valorEncontrado: value);
                        revisaoNecessaria = true;
                        origemSuspeita    = true;
                        divergencias++;
                    }
                }
            }
        }

        if (origemSuspeita || revisaoNecessaria)
            await _documentoService.AtualizarFlagsAuditoriaAsync(documentoId, origemSuspeita, revisaoNecessaria);

        // Regras A para guias
        if (doc.TipoDocumento == TipoDocumento.GuiaSPSADT)
            divergencias += await AuditarIntraGuiaAsync(documentoId);

        if (divergencias > 0)
            await _loteService.IncrementarDivergenciasAsync(doc.LoteId, divergencias);
        if (revisaoNecessaria)
            await _loteService.IncrementarRevisaoHumanaAsync(doc.LoteId);

        _logger.LogInformation("Auditoria doc {DocId}: {Div} divergências, revisão={Rev}", documentoId, divergencias, revisaoNecessaria);
        return divergencias;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGRA A — Intra-guia
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<int> AuditarIntraGuiaAsync(int documentoId)
    {
        var doc = await _repoDoc.GetByIdAsync(documentoId);
        if (doc == null || string.IsNullOrWhiteSpace(doc.DadosExtraidos)) return 0;

        int divergencias = 0;
        JObject? fields = null;
        try { fields = JObject.Parse(doc.DadosExtraidos); } catch { return 0; }

        // Usa ExtracaoJsonHelper para obter itens tipados com código + descrição normalizada
        var itensSolicitados = ExtracaoJsonHelper.ObterItens(fields, "itensSolicitados", "itensPedido");
        var itensRealizados  = ExtracaoJsonHelper.ObterItens(fields, "itensRealizados");

        // Totais monetários (campos com formato {amount: X, iso_4217_currency_code: "BRL"})
        var totalGeral         = ExtracaoJsonHelper.ObterCampoMonetario(fields, "totalGeral");
        var totalProcedimentos = ExtracaoJsonHelper.ObterCampoMonetario(fields, "totalProcedimentos");

        // A1: Item solicitado e não realizado
        foreach (var item in itensSolicitados)
        {
            if (!itensRealizados.Any(r => r.Corresponde(item)))
            {
                await _divService.CriarAsync(documentoId,
                    TipoDivergencia.ItemSolicitadoNaoRealizado, SeveridadeDivergencia.Alta,
                    $"Item solicitado '{item}' não encontrado nos itens realizados da guia.",
                    campoAfetado: "itensRealizados", valorEncontrado: "(ausente)", valorEsperado: item.ToString());
                divergencias++;
            }
        }

        // A2: Item realizado e não solicitado
        foreach (var item in itensRealizados)
        {
            if (!itensSolicitados.Any(s => s.Corresponde(item)))
            {
                await _divService.CriarAsync(documentoId,
                    TipoDivergencia.ItemRealizadoNaoSolicitado, SeveridadeDivergencia.Alta,
                    $"Item realizado '{item}' não consta nos itens solicitados da guia.",
                    campoAfetado: "itensSolicitados", valorEncontrado: item.ToString(), valorEsperado: "(ausente)");
                divergencias++;
            }
        }

        // A3: Quantidade divergente entre solicitado e realizado
        foreach (var solicitado in itensSolicitados)
        {
            var realizado = itensRealizados.FirstOrDefault(r => r.Corresponde(solicitado));
            if (realizado == null) continue;

            var qtdSol = solicitado.QuantidadeSolicitada ?? solicitado.QuantidadeAutorizada;
            var qtdReal = realizado.QuantidadeRealizada;

            if (qtdSol.HasValue && qtdReal.HasValue && Math.Abs(qtdReal.Value - qtdSol.Value) > 0.001)
            {
                await _divService.CriarAsync(documentoId,
                    TipoDivergencia.QuantidadeDivergente, SeveridadeDivergencia.Alta,
                    $"Quantidade divergente para '{solicitado}': solicitado={qtdSol:F0}, realizado={qtdReal:F0}.",
                    campoAfetado: solicitado.CodigoProcedimento ?? solicitado.DescricaoNormalizada ?? "item",
                    valorEncontrado: qtdReal.Value.ToString("F0"),
                    valorEsperado: qtdSol.Value.ToString("F0"));
                divergencias++;
            }
        }

        // A4: Soma dos valores dos itens realizados diferente do total geral (verificação monetária)
        // totalGeral é o campo monetário {amount: X} que representa o valor total da guia.
        // Não verifica quando totalGeral == 0 (guia de cortesia/convênio sem cobrança financeira).
        if (totalGeral.HasValue && totalGeral.Value > 0.01 && itensRealizados.Count > 0)
        {
            var somaItens = itensRealizados
                .Where(i => i.ValorTotal.HasValue)
                .Sum(i => i.ValorTotal!.Value);

            if (somaItens > 0 && Math.Abs(somaItens - totalGeral.Value) > 0.05)
            {
                await _divService.CriarAsync(documentoId,
                    TipoDivergencia.SomaDosItensDivergenteDoTotal, SeveridadeDivergencia.Alta,
                    $"Soma dos valores dos itens realizados (R$ {somaItens:F2}) difere do total geral (R$ {totalGeral:F2}).",
                    campoAfetado: "totalGeral",
                    valorEncontrado: somaItens.ToString("F2"),
                    valorEsperado: totalGeral.Value.ToString("F2"));
                divergencias++;
            }
        }

        // A5: Total de procedimentos incompatível com total geral
        if (totalProcedimentos.HasValue && totalGeral.HasValue && totalGeral.Value < totalProcedimentos.Value)
        {
            await _divService.CriarAsync(documentoId,
                TipoDivergencia.TotalProcedimentosIncompativelComTotalGeral, SeveridadeDivergencia.Critica,
                $"Total de procedimentos (R$ {totalProcedimentos:F2}) incompatível com total geral (R$ {totalGeral:F2}).",
                campoAfetado: "totalGeral",
                valorEncontrado: totalGeral.Value.ToString("F2"),
                valorEsperado: $">= {totalProcedimentos.Value:F2}");
            divergencias++;
        }

        return divergencias;
    }

    private static string? ExtrairValorAuditoria(object? valor)
    {
        if (valor == null)
            return null;

        // já é string
        if (valor is string s)
            return ExtrairValorAuditoriaDeTexto(s);

        // outros escalares
        if (valor is int or long or decimal or double or float or bool or DateTime)
            return valor.ToString();

        // tenta serializar e tratar como JSON
        try
        {
            var json = JsonConvert.SerializeObject(valor);
            return ExtrairValorAuditoriaDeTexto(json);
        }
        catch
        {
            return valor.ToString();
        }
    }

    private static string? ExtrairValorAuditoriaDeTexto(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return valor;

        var texto = valor.Trim();

        // se não parece JSON, devolve direto
        if (!texto.StartsWith("{") && !texto.StartsWith("["))
            return texto;

        try
        {
            var token = JToken.Parse(texto);
            return ExtrairValorUtil(token);
        }
        catch
        {
            return texto;
        }
    }

    private static string? ExtrairValorUtil(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return null;

        if (token.Type == JTokenType.String ||
            token.Type == JTokenType.Integer ||
            token.Type == JTokenType.Float ||
            token.Type == JTokenType.Boolean)
        {
            return token.ToString();
        }

        if (token is JObject obj)
        {
            // moeda
            if (obj["amount"] != null && obj["amount"]?.Type != JTokenType.Null)
                return obj["amount"]?.ToString();

            // item de procedimento/exame
            if (!string.IsNullOrWhiteSpace(obj["descricao_original"]?.ToString()))
                return obj["descricao_original"]?.ToString();

            if (!string.IsNullOrWhiteSpace(obj["descricao_normalizada"]?.ToString()))
                return obj["descricao_normalizada"]?.ToString();

            if (!string.IsNullOrWhiteSpace(obj["codigo_procedimento"]?.ToString()))
                return obj["codigo_procedimento"]?.ToString();

            // assinatura
            if (obj["is_signed"] != null && obj["is_signed"]?.Type != JTokenType.Null)
                return obj["is_signed"]?.ToString();

            // nomes úteis
            if (!string.IsNullOrWhiteSpace(obj["printed_name"]?.ToString()))
                return obj["printed_name"]?.ToString();

            if (!string.IsNullOrWhiteSpace(obj["nome"]?.ToString()))
                return obj["nome"]?.ToString();

            return "[JSON_COMPLEXO]";
        }

        if (token is JArray arr)
        {
            if (arr.Count == 0)
                return null;

            var valores = arr
                .Select(ExtrairValorUtil)
                .Where(v => !string.IsNullOrWhiteSpace(v) && v != "[JSON_COMPLEXO]")
                .ToList();

            if (valores.Count == 0)
                return "[JSON_COMPLEXO]";

            return string.Join(" | ", valores);
        }

        return "[JSON_COMPLEXO]";
    }

    private static string? NormalizarTextoBanco(string? valor, int max = 500)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return valor;

        var texto = valor.Trim();
        return texto.Length > max ? texto.Substring(0, max) : texto;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGRA B — Pedido x Guias
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<int> AuditarPedidoVsGuiasAsync(int atendimentoAgrupadoId)
    {
        var atendimento = await _repoAtendimento.GetByIdAsync(atendimentoAgrupadoId);
        if (atendimento == null) return 0;

        var todosDoc = await _repoDoc.GetAllAsync();
        var docs     = todosDoc.Where(d => d.AtendimentoAgrupadoId == atendimentoAgrupadoId).ToList();
        var pedidos  = docs.Where(d => d.TipoDocumento == TipoDocumento.PedidoMedico).ToList();
        var guias    = docs.Where(d => d.TipoDocumento == TipoDocumento.GuiaSPSADT).ToList();
        if (!pedidos.Any() || !guias.Any()) return 0;

        int divergencias = 0;
        var pedido = pedidos.First();

        JObject? fieldsPedido = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(pedido.DadosExtraidos))
                fieldsPedido = JObject.Parse(pedido.DadosExtraidos);
        }
        catch { }

        // Itens do pedido: tenta itensSolicitados e itensPedido (Pedido Médico usa itensPedido)
        var itensPedido    = ExtracaoJsonHelper.ObterItens(fieldsPedido, "itensSolicitados", "itensPedido");
        var crmPedido      = ExtracaoJsonHelper.ObterCampoTexto(fieldsPedido, "crm", "crmMedico");
        var pacientePedido = ExtracaoJsonHelper.ObterCampoTexto(fieldsPedido, "nomePaciente");
        var dataPedido     = ExtracaoJsonHelper.ObterCampoTexto(fieldsPedido, "dataSolicitacao");

        // Consolida todos os itens realizados de todas as guias
        var itensGuias = new List<ItemExtraido>();

        foreach (var guia in guias)
        {
            JObject? fieldsGuia = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(guia.DadosExtraidos))
                    fieldsGuia = JObject.Parse(guia.DadosExtraidos);
            }
            catch { }

            var realizadosGuia = ExtracaoJsonHelper.ObterItens(fieldsGuia, "itensRealizados");
            itensGuias.AddRange(realizadosGuia);

            // B3: Item realizado sem autorização (não consta no pedido)
            foreach (var item in realizadosGuia)
            {
                if (!itensPedido.Any(p => p.Corresponde(item)))
                {
                    await _divService.CriarAsync(guia.Id,
                        TipoDivergencia.ItemRealizadoSemAutorizacao, SeveridadeDivergencia.Critica,
                        $"Item '{item}' realizado/cobrado na guia sem constar no pedido médico.",
                        atendimentoAgrupadoId: atendimentoAgrupadoId,
                        campoAfetado: "itensRealizados", valorEncontrado: item.ToString());
                    divergencias++;
                }
            }

            // B6: CRM divergente entre pedido e guia
            var crmGuia = ExtracaoJsonHelper.ObterCampoTexto(fieldsGuia, "crm", "crmMedico");
            if (!string.IsNullOrWhiteSpace(crmPedido) && !string.IsNullOrWhiteSpace(crmGuia)
                && ExtracaoJsonHelper.NormalizarTexto(crmPedido) != ExtracaoJsonHelper.NormalizarTexto(crmGuia))
            {
                await _divService.CriarAsync(guia.Id,
                    TipoDivergencia.CrmDivergente, SeveridadeDivergencia.Critica,
                    $"CRM divergente: pedido='{crmPedido}', guia='{crmGuia}'.",
                    atendimentoAgrupadoId: atendimentoAgrupadoId,
                    campoAfetado: "crm", valorEncontrado: crmGuia, valorEsperado: crmPedido);
                divergencias++;
            }

            // B7: Paciente divergente entre pedido e guia
            var pacienteGuia = ExtracaoJsonHelper.ObterCampoTexto(fieldsGuia, "nomePaciente");
            if (!string.IsNullOrWhiteSpace(pacientePedido) && !string.IsNullOrWhiteSpace(pacienteGuia)
                && !ExtracaoJsonHelper.NomesSimilares(pacientePedido, pacienteGuia))
            {
                await _divService.CriarAsync(guia.Id,
                    TipoDivergencia.PacienteDivergente, SeveridadeDivergencia.Critica,
                    $"Paciente divergente: pedido='{pacientePedido}', guia='{pacienteGuia}'.",
                    atendimentoAgrupadoId: atendimentoAgrupadoId,
                    campoAfetado: "nomePaciente", valorEncontrado: pacienteGuia, valorEsperado: pacientePedido);
                divergencias++;
            }

            // B8: Data suspeita (guia anterior ao pedido)
            var dataGuia = ExtracaoJsonHelper.ObterCampoTexto(fieldsGuia, "dataAtendimento", "dataRealizacao");
            if (!string.IsNullOrWhiteSpace(dataPedido) && !string.IsNullOrWhiteSpace(dataGuia)
                && DateTime.TryParse(dataPedido, out var dtPedido)
                && DateTime.TryParse(dataGuia, out var dtGuia)
                && dtGuia < dtPedido)
            {
                await _divService.CriarAsync(guia.Id,
                    TipoDivergencia.DataSuspeitaPedidoGuia, SeveridadeDivergencia.Alta,
                    $"Data da guia ({dtGuia:dd/MM/yyyy}) é anterior à data do pedido ({dtPedido:dd/MM/yyyy}).",
                    atendimentoAgrupadoId: atendimentoAgrupadoId,
                    campoAfetado: "dataAtendimento",
                    valorEncontrado: dtGuia.ToString("dd/MM/yyyy"),
                    valorEsperado: $">= {dtPedido:dd/MM/yyyy}");
                divergencias++;
            }
        }

        // B1: Item do pedido não encontrado em nenhuma guia
        foreach (var item in itensPedido)
        {
            if (!itensGuias.Any(g => g.Corresponde(item)))
            {
                await _divService.CriarAsync(pedido.Id,
                    TipoDivergencia.ItemPedidoNaoEncontradoEmGuia, SeveridadeDivergencia.Alta,
                    $"Item do pedido '{item}' não encontrado em nenhuma guia do agrupamento.",
                    atendimentoAgrupadoId: atendimentoAgrupadoId,
                    campoAfetado: "itensSolicitados",
                    valorEncontrado: "(ausente)", valorEsperado: item.ToString());
                divergencias++;
            }
        }

        return divergencias;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGRA E — Duplicidade
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<int> DetectarDuplicidadesAsync(int loteId)
    {
        var todos    = await _repoDoc.GetAllAsync();
        var docsLote = todos.Where(d => d.LoteId == loteId).ToList();
        int divergencias = 0;

        // E1: Mesmo arquivo no mesmo lote
        var grupos = docsLote
            .GroupBy(d => $"{d.TipoDocumento}|{NormalizarNomeArquivo(d.NomeArquivo)}")
            .Where(g => g.Count() > 1);

        foreach (var grupo in grupos)
            foreach (var doc in grupo.Skip(1))
            {
                await _divService.CriarAsync(doc.Id,
                    TipoDivergencia.DuplicidadeNoLote, SeveridadeDivergencia.Alta,
                    $"Documento duplicado no lote: '{doc.NomeArquivo}'.",
                    campoAfetado: "NomeArquivo", valorEncontrado: doc.NomeArquivo);
                divergencias++;
            }

        // E2: Duplicidade entre lotes próximos
        if (_options.JanelaDuplicidadeDias > 0)
        {
            var lote = await _repoLote.GetByIdAsync(loteId);
            if (lote != null)
            {
                var janela    = lote.DataCriacao.AddDays(-_options.JanelaDuplicidadeDias);
                var outrosLotes = todos.Where(d => d.LoteId != loteId && d.DataCriacao >= janela).ToList();
                foreach (var doc in docsLote)
                {
                    var chave = NormalizarNomeArquivo(doc.NomeArquivo);
                    var dup   = outrosLotes.FirstOrDefault(d =>
                        d.TipoDocumento == doc.TipoDocumento && NormalizarNomeArquivo(d.NomeArquivo) == chave);
                    if (dup != null)
                    {
                        await _divService.CriarAsync(doc.Id,
                            TipoDivergencia.DuplicidadeEntreLotes, SeveridadeDivergencia.Alta,
                            $"Documento '{doc.NomeArquivo}' já existe em lote anterior (loteId={dup.LoteId}) dentro da janela de {_options.JanelaDuplicidadeDias} dias.",
                            campoAfetado: "NomeArquivo", valorEncontrado: doc.NomeArquivo);
                        divergencias++;
                    }
                }
            }
        }

        return divergencias;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGRA F — Ausência documental
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Detecta ausência documental cruzando procedimentos entre pedidos e guias.
    ///
    /// Regra de negócio:
    /// - Um pedido médico NÃO é considerado "sem guia" se QUALQUER guia do mesmo
    ///   lote cobrir ao menos um dos procedimentos do pedido (mesmo paciente).
    /// - Um pedido só gera divergência PedidoSemGuia se não houver NENHUMA guia
    ///   no lote com procedimentos em comum com o pedido do mesmo paciente.
    /// - Isso suporta o cenário real: 1 pedido → N guias (cada guia cobre parte
    ///   dos procedimentos do pedido).
    /// </summary>
    public async Task<int> DetectarAusenciaDocumentalAsync(int loteId)
    {
        var todos            = await _repoDoc.GetAllAsync();
        var docsLote         = todos.Where(d => d.LoteId == loteId).ToList();
        var atendimentos     = await _repoAtendimento.GetAllAsync();
        var atendimentosLote = atendimentos.Where(a => a.LoteId == loteId).ToList();
        int divergencias     = 0;

        foreach (var atendimento in atendimentosLote)
        {
            var docsAtend = docsLote.Where(d => d.AtendimentoAgrupadoId == atendimento.Id).ToList();
            var pedidos   = docsAtend.Where(d => d.TipoDocumento == TipoDocumento.PedidoMedico).ToList();
            var guias     = docsAtend.Where(d => d.TipoDocumento == TipoDocumento.GuiaSPSADT).ToList();

            // F1: Guia sem nenhum pedido no atendimento
            if (guias.Any() && !pedidos.Any())
            {
                var guia = guias.First();
                await _divService.CriarAsync(guia.Id,
                    TipoDivergencia.GuiaSemPedido, SeveridadeDivergencia.Alta,
                    $"Guia sem pedido médico correspondente no atendimento '{atendimento.NumeroGuia ?? atendimento.Id.ToString()}'.",
                    atendimentoAgrupadoId: atendimento.Id);
                divergencias++;
            }

            // F2: Pedido sem nenhuma guia no atendimento
            // Mas SOMENTE se não houver guias com procedimentos em comum com o pedido
            // em QUALQUER atendimento do mesmo lote (mesmo paciente).
            if (pedidos.Any() && !guias.Any())
            {
                foreach (var pedido in pedidos)
                {
                    JObject? fieldsPedido = null;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(pedido.DadosExtraidos))
                            fieldsPedido = JObject.Parse(pedido.DadosExtraidos);
                    }
                    catch { }

                    var itensPedido    = ExtracaoJsonHelper.ObterItens(fieldsPedido, "itensSolicitados", "itensPedido");
                    var pacientePedido = ExtracaoJsonHelper.ObterCampoTexto(fieldsPedido, "nomePaciente");

                    // Busca guias de QUALQUER atendimento do mesmo lote
                    var guiasLote         = docsLote.Where(d => d.TipoDocumento == TipoDocumento.GuiaSPSADT).ToList();
                    bool pedidoCoberto    = false;

                    foreach (var guia in guiasLote)
                    {
                        JObject? fieldsGuia = null;
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(guia.DadosExtraidos))
                                fieldsGuia = JObject.Parse(guia.DadosExtraidos);
                        }
                        catch { }
                        if (fieldsGuia == null) continue;

                        // Verifica se o paciente é o mesmo
                        var pacienteGuia = ExtracaoJsonHelper.ObterCampoTexto(fieldsGuia, "nomePaciente");
                        if (!string.IsNullOrWhiteSpace(pacientePedido) && !string.IsNullOrWhiteSpace(pacienteGuia)
                            && !ExtracaoJsonHelper.NomesSimilares(pacientePedido, pacienteGuia))
                            continue; // Paciente diferente — esta guia não é do mesmo paciente

                        // Verifica se há ao menos um procedimento em comum
                        var itensGuia = ExtracaoJsonHelper.ObterItens(fieldsGuia, "itensRealizados", "itensSolicitados");

                        if (itensPedido.Count > 0)
                        {
                            // Há procedimentos no pedido: verifica intersecção
                            if (itensGuia.Any(g => itensPedido.Any(p => p.Corresponde(g))))
                            {
                                pedidoCoberto = true;
                                break;
                            }
                        }
                        else
                        {
                            // Pedido sem itens extraídos (OCR falhou): considera coberto se
                            // há guia do mesmo paciente no lote (evita falso positivo).
                            if (!string.IsNullOrWhiteSpace(pacientePedido) && !string.IsNullOrWhiteSpace(pacienteGuia)
                                && ExtracaoJsonHelper.NomesSimilares(pacientePedido, pacienteGuia))
                            {
                                pedidoCoberto = true;
                                break;
                            }
                        }
                    }

                    if (!pedidoCoberto)
                    {
                        await _divService.CriarAsync(pedido.Id,
                            TipoDivergencia.PedidoSemGuia, SeveridadeDivergencia.Alta,
                            $"Pedido médico sem guia correspondente no atendimento '{atendimento.NumeroPedido ?? atendimento.Id.ToString()}'.",
                            atendimentoAgrupadoId: atendimento.Id);
                        divergencias++;
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Regra F: Pedido {DocId} considerado coberto por guia(s) do lote via cruzamento de procedimentos/paciente.",
                            pedido.Id);
                    }
                }
            }

            // F3: Ambos presentes → executa regras B (cruzamento pedido x guias)
            // CORREÇÃO: o retorno de AuditarPedidoVsGuiasAsync era ignorado anteriormente.
            if (pedidos.Any() && guias.Any())
                divergencias += await AuditarPedidoVsGuiasAsync(atendimento.Id);
        }

        return divergencias;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGRA G — Score de risco
    // ─────────────────────────────────────────────────────────────────────────
    public async Task CalcularScoreRiscoAsync(int loteId)
    {
        var atendimentos     = await _repoAtendimento.GetAllAsync();
        var atendimentosLote = atendimentos.Where(a => a.LoteId == loteId).ToList();
        var todosDoc         = await _repoDoc.GetAllAsync();

        foreach (var atendimento in atendimentosLote)
        {
            var docsAtend = todosDoc.Where(d => d.AtendimentoAgrupadoId == atendimento.Id).ToList();
            double score  = 0;

            score += atendimento.QuantidadeDivergencias * 5;

            var confiancaMedia = docsAtend.Any() ? docsAtend.Average(d => d.ConfiancaOcr) : 1.0;
            if (confiancaMedia < 0.80) score += 20;
            else if (confiancaMedia < 0.90) score += 10;

            if (docsAtend.Any(d => d.OrigemSuspeita))           score += 25;
            if (docsAtend.Any(d => d.RevisaoHumanaNecessaria))  score += 15;

            var temPedido = docsAtend.Any(d => d.TipoDocumento == TipoDocumento.PedidoMedico);
            var temGuia   = docsAtend.Any(d => d.TipoDocumento == TipoDocumento.GuiaSPSADT);
            if (!temPedido || !temGuia) score += 30;

            atendimento.ScoreRisco             = Math.Min(score, 100);
            atendimento.RevisaoHumanaNecessaria = score >= 50;
            atendimento.DataAtualizacao         = DateTime.UtcNow;
            await _repoAtendimento.UpdateAsync(atendimento);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers privados
    // ─────────────────────────────────────────────────────────────────────────

    private static string NormalizarNomeArquivo(string s) =>
        System.IO.Path.GetFileNameWithoutExtension(s).ToLowerInvariant().Trim();
}

/// <summary>Opções de configuração para as regras de auditoria.</summary>
public class AuditoriaOptions
{
    public const string SectionName = "Auditoria";
    public ModoAuditoria ModoAuditoria { get; set; } = ModoAuditoria.Assistido;
    /// <summary>Janela em dias para detecção de duplicidade entre lotes. 0 = desabilitado.</summary>
    public int JanelaDuplicidadeDias { get; set; } = 30;
}
