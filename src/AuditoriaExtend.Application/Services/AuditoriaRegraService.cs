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

    // Campos criticos que exigem revisao humana obrigatoria
    // Inclui tanto camelCase (formato interno normalizado) quanto snake_case (formato bruto da Extend)
    private static readonly HashSet<string> CamposCriticos = new(StringComparer.OrdinalIgnoreCase)
    {
        // camelCase (formato normalizado pelo WebhookProcessorService)
        "nomePaciente", "crm", "crmMedico", "numeroGuia", "numeroPedido",
        "dataSolicitacao", "dataAtendimento", "codigoProcedimento", "procedimento",
        "quantidadeSolicitada", "quantidadeRealizada", "totalGeral",
        "numeroCarteira", "itensSolicitados", "itensRealizados",
        // snake_case (formato bruto da Extend - armazenado em paralelo pelo NormalizarCampos)
        "nome_paciente", "nome_beneficiario", "crm_medico",
        "numero_guia", "numero_guia_prestador", "numero_pedido",
        "data_solicitacao", "data_atendimento", "data_realizacao",
        "codigo_procedimento", "quantidade_solicitada", "quantidade_realizada",
        "total_geral", "numero_carteira", "itens_solicitados", "itens_realizados"
    };

    // Campos importantes que geram alerta
    // Inclui tanto camelCase quanto snake_case
    private static readonly HashSet<string> CamposImportantes = new(StringComparer.OrdinalIgnoreCase)
    {
        // camelCase
        "nomeMedico", "especialidade", "cid", "indicacaoClinica", "valorUnitario", "valorTotal",
        "codigoCnes", "totalOpme", "totalProcedimentos",
        // snake_case
        "nome_medico", "nome_solicitante", "especialidade", "cid",
        "indicacao_clinica", "diagnostico", "valor_unitario", "valor_total",
        "codigo_cnes", "cnes", "total_opme", "total_procedimentos"
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

        // Suporta tanto camelCase (normalizado) quanto snake_case (bruto da Extend)
        var itensSolicitados = ExtrairListaItensMulti(fields, "itensSolicitados", "itens_solicitados", "procedimentos");
        var itensRealizados  = ExtrairListaItensMulti(fields, "itensRealizados",  "itens_realizados");
        var totalProcedimentos = ExtrairValorNumerico(fields, "totalProcedimentos", "total_procedimentos");
        var totalGeral         = ExtrairValorNumerico(fields, "totalGeral", "total_geral");
        var quantsSolicitadas = ExtrairQuantidadesMulti(fields, "quantidadesSolicitadas", "quantidades_solicitadas");
        var quantsRealizadas  = ExtrairQuantidadesMulti(fields, "quantidadesRealizadas",  "quantidades_realizadas");

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

        var itensPedido = fieldsPedido != null ? ExtrairListaItensMulti(fieldsPedido, "itensSolicitados", "itens_solicitados", "procedimentos") : new List<string>();
        var crmPedido      = ExtrairCampoMulti(fieldsPedido, "crm", "crm_medico");
        var pacientePedido = ExtrairCampoMulti(fieldsPedido, "nomePaciente", "nome_paciente", "nome_beneficiario");
        var dataPedido     = ExtrairCampoMulti(fieldsPedido, "dataSolicitacao", "data_solicitacao");
        var itensGuias = new List<string>();

        foreach (var guia in guias)
        {
            JObject? fieldsGuia = null;
            try { if (!string.IsNullOrWhiteSpace(guia.DadosExtraidos)) fieldsGuia = JObject.Parse(guia.DadosExtraidos); } catch { }
            if (fieldsGuia != null)
                itensGuias.AddRange(ExtrairListaItensMulti(fieldsGuia, "itensRealizados", "itens_realizados"));

            // B3: Item realizado sem autorização
            var realizadosGuia = fieldsGuia != null ? ExtrairListaItensMulti(fieldsGuia, "itensRealizados", "itens_realizados") : new List<string>();
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
            var crmGuia = ExtrairCampoMulti(fieldsGuia, "crm", "crm_medico");
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
            var pacienteGuia = ExtrairCampoMulti(fieldsGuia, "nomePaciente", "nome_paciente", "nome_beneficiario");
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
            var dataGuia = ExtrairCampoMulti(fieldsGuia, "dataAtendimento", "data_atendimento", "data_realizacao");
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
    /// <summary>
    /// Detecta ausência documental cruzando procedimentos entre pedidos e guias.
    ///
    /// Regra de negócio:
    /// - Um pedido médico NÃO é considerado "sem guia" se QUALQUER guia do mesmo
    ///   atendimento agrupado cobrir ao menos um dos procedimentos do pedido.
    /// - Um pedido só gera divergência PedidoSemGuia se não houver NENHUMA guia
    ///   no atendimento E não houver guias com procedimentos em comum.
    /// - Isso suporta o cenário real: 1 pedido → N guias (cada guia cobre parte
    ///   dos procedimentos do pedido).
    /// </summary>
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
            // Mas SOMENTE se não houver guias com procedimentos em comum com o pedido.
            // Isso evita falso positivo quando o pedido tem guias mas o agrupamento
            // não capturou o vínculo explícito (ex: pedido e guias agrupados pelo mesmo paciente).
            if (pedidos.Any() && !guias.Any())
            {
                foreach (var pedido in pedidos)
                {
                    JObject? fieldsPedido = null;
                    try { if (!string.IsNullOrWhiteSpace(pedido.DadosExtraidos)) fieldsPedido = JObject.Parse(pedido.DadosExtraidos); } catch { }

                    var itensPedido = fieldsPedido != null
                        ? ExtrairListaItensMulti(fieldsPedido, "itensSolicitados", "itens_solicitados", "procedimentos", "itens_pedido")
                        : new List<string>();

                    var pacientePedido = ExtrairCampoMulti(fieldsPedido, "nomePaciente", "nome_paciente", "nome_beneficiario");

                    // Busca guias de QUALQUER atendimento do mesmo lote que tenham o mesmo paciente
                    // e ao menos um procedimento em comum com o pedido.
                    var guiasLote = docsLote.Where(d => d.TipoDocumento == TipoDocumento.GuiaSPSADT).ToList();

                    bool pedidoCobertoPorGuia = false;

                    foreach (var guia in guiasLote)
                    {
                        JObject? fieldsGuia = null;
                        try { if (!string.IsNullOrWhiteSpace(guia.DadosExtraidos)) fieldsGuia = JObject.Parse(guia.DadosExtraidos); } catch { }
                        if (fieldsGuia == null) continue;

                        // Verifica se o paciente é o mesmo (se ambos tiverem nome)
                        var pacienteGuia = ExtrairCampoMulti(fieldsGuia, "nomePaciente", "nome_paciente", "nome_beneficiario");
                        if (!string.IsNullOrWhiteSpace(pacientePedido) && !string.IsNullOrWhiteSpace(pacienteGuia)
                            && !NomesSimiliares(pacientePedido, pacienteGuia))
                            continue; // Paciente diferente — esta guia não é do mesmo paciente

                        // Verifica se há ao menos um procedimento em comum
                        var itensGuia = ExtrairListaItensMulti(fieldsGuia, "itensRealizados", "itens_realizados",
                                                               "itensSolicitados", "itens_solicitados");

                        if (itensPedido.Any())
                        {
                            // Há procedimentos no pedido: verifica intersecção
                            if (itensGuia.Any(g => itensPedido.Any(p => NormalizarCodigo(p) == NormalizarCodigo(g))))
                            {
                                pedidoCobertoPorGuia = true;
                                break;
                            }
                        }
                        else
                        {
                            // Pedido sem itens extraídos (OCR falhou): considera coberto se
                            // há guia do mesmo paciente no lote (não gera falso positivo).
                            if (!string.IsNullOrWhiteSpace(pacientePedido) && !string.IsNullOrWhiteSpace(pacienteGuia)
                                && NomesSimiliares(pacientePedido, pacienteGuia))
                            {
                                pedidoCobertoPorGuia = true;
                                break;
                            }
                        }
                    }

                    if (!pedidoCobertoPorGuia)
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
            if (pedidos.Any() && guias.Any())
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

    /// <summary>
    /// Extrai lista de itens tentando multiplos nomes de campo (camelCase e snake_case).
    /// Retorna a primeira lista nao-vazia encontrada.
    /// </summary>
    private static List<string> ExtrairListaItensMulti(JObject fields, params string[] nomesCampo)
    {
        foreach (var nome in nomesCampo)
        {
            var lista = ExtrairListaItens(fields, nome);
            if (lista.Count > 0) return lista;
        }
        return new List<string>();
    }

    /// <summary>
    /// Extrai dicionario de quantidades tentando multiplos nomes de campo.
    /// </summary>
    private static Dictionary<string, double> ExtrairQuantidadesMulti(JObject fields, params string[] nomesCampo)
    {
        foreach (var nome in nomesCampo)
        {
            var dict = ExtrairQuantidades(fields, nome);
            if (dict.Count > 0) return dict;
        }
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extrai valor numerico tentando multiplos nomes de campo.
    /// </summary>
    private static double? ExtrairValorNumerico(JObject fields, params string[] nomesCampo)
    {
        foreach (var nome in nomesCampo)
        {
            var token = fields[nome];
            if (token == null || token.Type == JTokenType.Null)
                continue;

            // número direto
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<double>();

            // string numérica
            if (token.Type == JTokenType.String &&
                double.TryParse(token.ToString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var valorTexto))
                return valorTexto;

            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;

                // Formato normalizado do WebhookProcessorService: { "value": X, "confidence": Y }
                // O campo "value" pode ser escalar ou objeto { "amount": N, "iso_4217_currency_code": "BRL" }
                var valueToken = obj["value"];
                if (valueToken != null && valueToken.Type != JTokenType.Null)
                {
                    // value é escalar numérico
                    if (valueToken.Type == JTokenType.Float || valueToken.Type == JTokenType.Integer)
                        return valueToken.Value<double>();

                    // value é string numérica
                    if (valueToken.Type == JTokenType.String &&
                        double.TryParse(valueToken.ToString(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var vt))
                        return vt;

                    // value é objeto { "amount": N } (formato monetário da Extend)
                    if (valueToken.Type == JTokenType.Object)
                    {
                        var innerObj = (JObject)valueToken;
                        var amountInner = innerObj["amount"];
                        if (amountInner != null && (amountInner.Type == JTokenType.Float || amountInner.Type == JTokenType.Integer))
                            return amountInner.Value<double>();
                        if (amountInner?.Type == JTokenType.String &&
                            double.TryParse(amountInner.ToString(), System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var amt))
                            return amt;
                    }
                }

                // Objeto direto com "amount" (sem wrapper value) — formato bruto da Extend
                var directAmount = obj["amount"];
                if (directAmount != null && (directAmount.Type == JTokenType.Float || directAmount.Type == JTokenType.Integer))
                    return directAmount.Value<double>();
                if (directAmount?.Type == JTokenType.String &&
                    double.TryParse(directAmount.ToString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var da))
                    return da;
            }
        }

        return null;
    }

    /// <summary>
    /// Extrai valor de texto de um campo, tentando multiplos nomes (camelCase e snake_case).
    /// Suporta tanto o formato normalizado { "value": "..." } quanto valor direto.
    /// </summary>
    private static string? ExtrairCampoMulti(JObject? fields, params string[] nomesCampo)
    {
        if (fields == null) return null;
        foreach (var nome in nomesCampo)
        {
            var token = fields[nome];
            if (token == null) continue;
            if (token is JObject obj)
            {
                var val = obj["value"];
                if (val != null && val.Type != JTokenType.Null
                    && val.Type != JTokenType.Array && val.Type != JTokenType.Object)
                {
                    var str = val.ToString();
                    if (!string.IsNullOrWhiteSpace(str)) return str;
                }
            }
            else if (token.Type != JTokenType.Null
                     && token.Type != JTokenType.Array && token.Type != JTokenType.Object)
            {
                var str = token.ToString();
                if (!string.IsNullOrWhiteSpace(str)) return str;
            }
        }
        return null;
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
