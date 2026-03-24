using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;

namespace AuditoriaExtend.Application.Services;

/// <summary>
/// Implementa todas as regras de auditoria após o retorno da Extend.
/// A=IntraGuia | B=PedidoVsGuia | C=Confiança | D=OrigemSuspeita | E=Duplicidade | F=AusenciaDocumental | G=ScoreRisco
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
    private const double LimiarOcrCriticoObrigatorio = 0.80;
    private const double LimiarOcrImportanteAlerta = 0.90;
    private const int LimiarReviewScoreBaixo = 3;
    private const int LimiarReviewScoreAlerta = 4;

    // Campos críticos que exigem revisão humana obrigatória
    private static readonly HashSet<string> CamposCriticos = new(StringComparer.OrdinalIgnoreCase)
    {
        "nomePaciente", "crm", "crmMedico", "numeroGuia", "numeroPedido",
        "dataSolicitacao", "dataAtendimento", "codigoProcedimento", "procedimento",
        "quantidadeSolicitada", "quantidadeRealizada", "totalGeral"
    };

    // Campos importantes que geram alerta
    private static readonly HashSet<string> CamposImportantes = new(StringComparer.OrdinalIgnoreCase)
    {
        "nomeMedico", "especialidade", "cid", "indicacaoClinica", "valorUnitario", "valorTotal"
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
        bool origemSuspeita = false;

        JObject? fields = null;
        try { fields = JObject.Parse(doc.DadosExtraidos); } catch { /* JSON inválido */ }

        if (fields != null)
        {
            foreach (var prop in fields.Properties())
            {
                var nomeCampo = prop.Name;
                var fieldObj = prop.Value as JObject;
                if (fieldObj == null) continue;

                var ocrConf = fieldObj["confidence"]?.Value<double?>();
                var reviewScore = fieldObj["reviewAgentScore"]?.Value<int?>();
                var citation = fieldObj["citation"]?.ToString();
                var pageNumber = fieldObj["pageNumber"]?.Value<int?>();
                var isPrintedPage = fieldObj["isPrintedPage"]?.Value<bool?>() ?? false;
                var value = fieldObj["value"]?.ToString();

                bool ehCritico = CamposCriticos.Contains(nomeCampo);
                bool ehImportante = CamposImportantes.Contains(nomeCampo);

                // C1: reviewAgentScore <= 3 em campo crítico → revisão obrigatória
                if (ehCritico && reviewScore.HasValue && reviewScore.Value <= LimiarReviewScoreBaixo)
                {
                    await _divService.CriarAsync(documentoId,
                        TipoDivergencia.CampoCriticoReviewScoreBaixo, SeveridadeDivergencia.Critica,
                        $"Campo crítico '{nomeCampo}' com reviewAgentScore={reviewScore} (≤ {LimiarReviewScoreBaixo}) — revisão obrigatória.",
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
                        $"Campo crítico '{nomeCampo}' com reviewAgentScore={reviewScore} — alerta de qualidade.",
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

                // C4: campo crítico sem citação
                if (ehCritico && string.IsNullOrWhiteSpace(citation) && !string.IsNullOrWhiteSpace(value))
                {
                    await _divService.CriarAsync(documentoId,
                        TipoDivergencia.CampoCriticoSemCitacao, SeveridadeDivergencia.Media,
                        $"Campo crítico '{nomeCampo}' sem citação no documento original.",
                        campoAfetado: nomeCampo, valorEncontrado: value);
                    divergencias++;
                }

                // D: Origem suspeita — item ancorado em página impressa posterior
                if (doc.TipoDocumento == TipoDocumento.PedidoMedico && isPrintedPage)
                {
                    if (_options.ModoAuditoria == ModoAuditoria.Estrito)
                    {
                        await _divService.CriarAsync(documentoId,
                            TipoDivergencia.OrigemSuspeita, SeveridadeDivergencia.Critica,
                            $"[MODO ESTRITO] Campo '{nomeCampo}' encontrado em página impressa posterior (pág. {pageNumber}) — item rejeitado.",
                            campoAfetado: nomeCampo, valorEncontrado: value);
                        revisaoNecessaria = true;
                        origemSuspeita = true;
                        divergencias++;
                    }
                    else
                    {
                        await _divService.CriarAsync(documentoId,
                            TipoDivergencia.OrigemSuspeita, SeveridadeDivergencia.Alta,
                            $"[MODO ASSISTIDO] Campo '{nomeCampo}' encontrado em página impressa posterior (pág. {pageNumber}) — alerta de origem suspeita.",
                            campoAfetado: nomeCampo, valorEncontrado: value);
                        revisaoNecessaria = true;
                        origemSuspeita = true;
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

        var itensSolicitados = ExtrairListaItens(fields, "itensSolicitados");
        var itensRealizados = ExtrairListaItens(fields, "itensRealizados");
        var totalProcedimentos = fields["totalProcedimentos"]?["value"]?.Value<double?>();
        var totalGeral = fields["totalGeral"]?["value"]?.Value<double?>();
        var quantsSolicitadas = ExtrairQuantidades(fields, "quantidadesSolicitadas");
        var quantsRealizadas = ExtrairQuantidades(fields, "quantidadesRealizadas");

        // A1: Item solicitado e não realizado
        foreach (var item in itensSolicitados)
        {
            if (!itensRealizados.Any(r => NormalizarCodigo(r) == NormalizarCodigo(item)))
            {
                await _divService.CriarAsync(documentoId,
                    TipoDivergencia.ItemSolicitadoNaoRealizado, SeveridadeDivergencia.Alta,
                    $"Item solicitado '{item}' não encontrado nos itens realizados da guia.",
                    campoAfetado: "itensRealizados", valorEncontrado: "(ausente)", valorEsperado: item);
                divergencias++;
            }
        }

        // A2: Item realizado e não solicitado
        foreach (var item in itensRealizados)
        {
            if (!itensSolicitados.Any(s => NormalizarCodigo(s) == NormalizarCodigo(item)))
            {
                await _divService.CriarAsync(documentoId,
                    TipoDivergencia.ItemRealizadoNaoSolicitado, SeveridadeDivergencia.Alta,
                    $"Item realizado '{item}' não consta nos itens solicitados da guia.",
                    campoAfetado: "itensSolicitados", valorEncontrado: item, valorEsperado: "(ausente)");
                divergencias++;
            }
        }

        // A3: Quantidade divergente
        foreach (var kvp in quantsSolicitadas)
        {
            if (quantsRealizadas.TryGetValue(kvp.Key, out var realizada) && Math.Abs(realizada - kvp.Value) > 0.001)
            {
                await _divService.CriarAsync(documentoId,
                    TipoDivergencia.QuantidadeDivergente, SeveridadeDivergencia.Alta,
                    $"Quantidade divergente para '{kvp.Key}': solicitado={kvp.Value}, realizado={realizada}.",
                    campoAfetado: kvp.Key, valorEncontrado: realizada.ToString("F0"), valorEsperado: kvp.Value.ToString("F0"));
                divergencias++;
            }
        }

        // A4: Soma dos itens diferente do total de procedimentos
        if (totalProcedimentos.HasValue && quantsRealizadas.Count > 0)
        {
            var soma = quantsRealizadas.Values.Sum();
            if (Math.Abs(soma - totalProcedimentos.Value) > 0.01)
            {
                await _divService.CriarAsync(documentoId,
                    TipoDivergencia.SomaDosItensDivergenteDoTotal, SeveridadeDivergencia.Alta,
                    $"Soma dos itens realizados ({soma}) difere do total de procedimentos ({totalProcedimentos}).",
                    campoAfetado: "totalProcedimentos", valorEncontrado: soma.ToString("F2"), valorEsperado: totalProcedimentos.Value.ToString("F2"));
                divergencias++;
            }
        }

        // A5: Total de procedimentos incompatível com total geral
        if (totalProcedimentos.HasValue && totalGeral.HasValue && totalGeral.Value < totalProcedimentos.Value)
        {
            await _divService.CriarAsync(documentoId,
                TipoDivergencia.TotalProcedimentosIncompativelComTotalGeral, SeveridadeDivergencia.Critica,
                $"Total de procedimentos ({totalProcedimentos}) incompatível com total geral ({totalGeral}).",
                campoAfetado: "totalGeral", valorEncontrado: totalGeral.Value.ToString("F2"), valorEsperado: $">= {totalProcedimentos.Value:F2}");
            divergencias++;
        }

        return divergencias;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGRA B — Pedido x Guias
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<int> AuditarPedidoVsGuiasAsync(int atendimentoAgrupadoId)
    {
        var atendimento = await _repoAtendimento.GetByIdAsync(atendimentoAgrupadoId);
        if (atendimento == null) return 0;

        var todosDoc = await _repoDoc.GetAllAsync();
        var docs = todosDoc.Where(d => d.AtendimentoAgrupadoId == atendimentoAgrupadoId).ToList();
        var pedidos = docs.Where(d => d.TipoDocumento == TipoDocumento.PedidoMedico).ToList();
        var guias = docs.Where(d => d.TipoDocumento == TipoDocumento.GuiaSPSADT).ToList();
        if (!pedidos.Any() || !guias.Any()) return 0;

        int divergencias = 0;
        var pedido = pedidos.First();
        JObject? fieldsPedido = null;
        try { if (!string.IsNullOrWhiteSpace(pedido.DadosExtraidos)) fieldsPedido = JObject.Parse(pedido.DadosExtraidos); } catch { }

        var itensPedido = fieldsPedido != null ? ExtrairListaItens(fieldsPedido, "itensSolicitados") : new List<string>();
        var crmPedido = fieldsPedido?["crm"]?["value"]?.ToString();
        var pacientePedido = fieldsPedido?["nomePaciente"]?["value"]?.ToString();
        var dataPedido = fieldsPedido?["dataSolicitacao"]?["value"]?.ToString();
        var itensGuias = new List<string>();

        foreach (var guia in guias)
        {
            JObject? fieldsGuia = null;
            try { if (!string.IsNullOrWhiteSpace(guia.DadosExtraidos)) fieldsGuia = JObject.Parse(guia.DadosExtraidos); } catch { }
            if (fieldsGuia != null)
                itensGuias.AddRange(ExtrairListaItens(fieldsGuia, "itensRealizados"));

            // B3: Item realizado sem autorização
            var realizadosGuia = fieldsGuia != null ? ExtrairListaItens(fieldsGuia, "itensRealizados") : new List<string>();
            foreach (var item in realizadosGuia)
            {
                if (!itensPedido.Any(p => NormalizarCodigo(p) == NormalizarCodigo(item)))
                {
                    await _divService.CriarAsync(guia.Id,
                        TipoDivergencia.ItemRealizadoSemAutorizacao, SeveridadeDivergencia.Critica,
                        $"Item '{item}' realizado/cobrado na guia sem constar no pedido médico.",
                        atendimentoAgrupadoId: atendimentoAgrupadoId, campoAfetado: "itensRealizados", valorEncontrado: item);
                    divergencias++;
                }
            }

            // B6: CRM divergente
            var crmGuia = fieldsGuia?["crm"]?["value"]?.ToString();
            if (!string.IsNullOrWhiteSpace(crmPedido) && !string.IsNullOrWhiteSpace(crmGuia)
                && NormalizarCodigo(crmPedido) != NormalizarCodigo(crmGuia))
            {
                await _divService.CriarAsync(guia.Id,
                    TipoDivergencia.CrmDivergente, SeveridadeDivergencia.Critica,
                    $"CRM divergente: pedido='{crmPedido}', guia='{crmGuia}'.",
                    atendimentoAgrupadoId: atendimentoAgrupadoId, campoAfetado: "crm",
                    valorEncontrado: crmGuia, valorEsperado: crmPedido);
                divergencias++;
            }

            // B7: Paciente divergente
            var pacienteGuia = fieldsGuia?["nomePaciente"]?["value"]?.ToString();
            if (!string.IsNullOrWhiteSpace(pacientePedido) && !string.IsNullOrWhiteSpace(pacienteGuia)
                && !NomesSimiliares(pacientePedido, pacienteGuia))
            {
                await _divService.CriarAsync(guia.Id,
                    TipoDivergencia.PacienteDivergente, SeveridadeDivergencia.Critica,
                    $"Paciente divergente: pedido='{pacientePedido}', guia='{pacienteGuia}'.",
                    atendimentoAgrupadoId: atendimentoAgrupadoId, campoAfetado: "nomePaciente",
                    valorEncontrado: pacienteGuia, valorEsperado: pacientePedido);
                divergencias++;
            }

            // B8: Data suspeita (guia anterior ao pedido)
            var dataGuia = fieldsGuia?["dataAtendimento"]?["value"]?.ToString();
            if (!string.IsNullOrWhiteSpace(dataPedido) && !string.IsNullOrWhiteSpace(dataGuia)
                && DateTime.TryParse(dataPedido, out var dtPedido)
                && DateTime.TryParse(dataGuia, out var dtGuia)
                && dtGuia < dtPedido)
            {
                await _divService.CriarAsync(guia.Id,
                    TipoDivergencia.DataSuspeitaPedidoGuia, SeveridadeDivergencia.Alta,
                    $"Data da guia ({dtGuia:dd/MM/yyyy}) é anterior à data do pedido ({dtPedido:dd/MM/yyyy}).",
                    atendimentoAgrupadoId: atendimentoAgrupadoId, campoAfetado: "dataAtendimento",
                    valorEncontrado: dtGuia.ToString("dd/MM/yyyy"), valorEsperado: $">= {dtPedido:dd/MM/yyyy}");
                divergencias++;
            }
        }

        // B1: Item do pedido não encontrado em nenhuma guia
        foreach (var item in itensPedido)
        {
            if (!itensGuias.Any(g => NormalizarCodigo(g) == NormalizarCodigo(item)))
            {
                await _divService.CriarAsync(pedido.Id,
                    TipoDivergencia.ItemPedidoNaoEncontradoEmGuia, SeveridadeDivergencia.Alta,
                    $"Item do pedido '{item}' não encontrado em nenhuma guia do agrupamento.",
                    atendimentoAgrupadoId: atendimentoAgrupadoId, campoAfetado: "itensSolicitados",
                    valorEncontrado: "(ausente)", valorEsperado: item);
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
        var todos = await _repoDoc.GetAllAsync();
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
                var janela = lote.DataCriacao.AddDays(-_options.JanelaDuplicidadeDias);
                var outrosLotes = todos.Where(d => d.LoteId != loteId && d.DataCriacao >= janela).ToList();
                foreach (var doc in docsLote)
                {
                    var chave = NormalizarNomeArquivo(doc.NomeArquivo);
                    var dup = outrosLotes.FirstOrDefault(d =>
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
    public async Task<int> DetectarAusenciaDocumentalAsync(int loteId)
    {
        var todos = await _repoDoc.GetAllAsync();
        var docsLote = todos.Where(d => d.LoteId == loteId).ToList();
        var atendimentos = await _repoAtendimento.GetAllAsync();
        var atendimentosLote = atendimentos.Where(a => a.LoteId == loteId).ToList();
        int divergencias = 0;

        foreach (var atendimento in atendimentosLote)
        {
            var docsAtend = docsLote.Where(d => d.AtendimentoAgrupadoId == atendimento.Id).ToList();
            var temPedido = docsAtend.Any(d => d.TipoDocumento == TipoDocumento.PedidoMedico);
            var temGuia = docsAtend.Any(d => d.TipoDocumento == TipoDocumento.GuiaSPSADT);

            if (temGuia && !temPedido)
            {
                var guia = docsAtend.First(d => d.TipoDocumento == TipoDocumento.GuiaSPSADT);
                await _divService.CriarAsync(guia.Id,
                    TipoDivergencia.GuiaSemPedido, SeveridadeDivergencia.Alta,
                    $"Guia sem pedido médico correspondente no atendimento '{atendimento.NumeroGuia ?? atendimento.Id.ToString()}'.",
                    atendimentoAgrupadoId: atendimento.Id);
                divergencias++;
            }

            if (temPedido && !temGuia)
            {
                var pedido = docsAtend.First(d => d.TipoDocumento == TipoDocumento.PedidoMedico);
                await _divService.CriarAsync(pedido.Id,
                    TipoDivergencia.PedidoSemGuia, SeveridadeDivergencia.Alta,
                    $"Pedido médico sem guia correspondente no atendimento '{atendimento.NumeroPedido ?? atendimento.Id.ToString()}'.",
                    atendimentoAgrupadoId: atendimento.Id);
                divergencias++;
            }

            if (temPedido && temGuia)
                await AuditarPedidoVsGuiasAsync(atendimento.Id);
        }

        return divergencias;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REGRA G — Score de risco
    // ─────────────────────────────────────────────────────────────────────────
    public async Task CalcularScoreRiscoAsync(int loteId)
    {
        var atendimentos = await _repoAtendimento.GetAllAsync();
        var atendimentosLote = atendimentos.Where(a => a.LoteId == loteId).ToList();
        var todosDoc = await _repoDoc.GetAllAsync();

        foreach (var atendimento in atendimentosLote)
        {
            var docsAtend = todosDoc.Where(d => d.AtendimentoAgrupadoId == atendimento.Id).ToList();
            double score = 0;

            score += atendimento.QuantidadeDivergencias * 5;

            var confiancaMedia = docsAtend.Any() ? docsAtend.Average(d => d.ConfiancaOcr) : 1.0;
            if (confiancaMedia < 0.80) score += 20;
            else if (confiancaMedia < 0.90) score += 10;

            if (docsAtend.Any(d => d.OrigemSuspeita)) score += 25;
            if (docsAtend.Any(d => d.RevisaoHumanaNecessaria)) score += 15;

            var temPedido = docsAtend.Any(d => d.TipoDocumento == TipoDocumento.PedidoMedico);
            var temGuia = docsAtend.Any(d => d.TipoDocumento == TipoDocumento.GuiaSPSADT);
            if (!temPedido || !temGuia) score += 30;

            atendimento.ScoreRisco = Math.Min(score, 100);
            atendimento.RevisaoHumanaNecessaria = score >= 50;
            atendimento.DataAtualizacao = DateTime.UtcNow;
            await _repoAtendimento.UpdateAsync(atendimento);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────
    private static List<string> ExtrairListaItens(JObject fields, string nomeCampo)
    {
        var token = fields[nomeCampo]?["value"];
        if (token == null) return new List<string>();
        if (token.Type == JTokenType.Array)
            return token.Select(t => t.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        var str = token.ToString();
        return string.IsNullOrWhiteSpace(str) ? new List<string>() : new List<string> { str };
    }

    private static Dictionary<string, double> ExtrairQuantidades(JObject fields, string nomeCampo)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var token = fields[nomeCampo]?["value"];
        if (token is JObject obj)
            foreach (var prop in obj.Properties())
                if (double.TryParse(prop.Value.ToString(), out var val))
                    result[prop.Name] = val;
        return result;
    }

    private static string NormalizarCodigo(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string NormalizarNomeArquivo(string s) =>
        System.IO.Path.GetFileNameWithoutExtension(s).ToLowerInvariant().Trim();

    private static bool NomesSimiliares(string a, string b)
    {
        var na = a.ToUpperInvariant().Trim();
        var nb = b.ToUpperInvariant().Trim();
        return na == nb || na.Contains(nb) || nb.Contains(na);
    }
}

/// <summary>Opções de configuração para as regras de auditoria.</summary>
public class AuditoriaOptions
{
    public const string SectionName = "Auditoria";
    public ModoAuditoria ModoAuditoria { get; set; } = ModoAuditoria.Assistido;
    /// <summary>Janela em dias para detecção de duplicidade entre lotes. 0 = desabilitado.</summary>
    public int JanelaDuplicidadeDias { get; set; } = 30;
}
