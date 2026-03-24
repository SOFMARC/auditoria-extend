namespace AuditoriaExtend.Domain.Enums;

/// <summary>Status do lote de importação.</summary>
public enum StatusLote
{
    Pendente = 0,
    Processando = 1,
    AguardandoExtend = 2,
    Concluido = 3,
    Erro = 4
}

/// <summary>Tipo de documento médico.</summary>
public enum TipoDocumento
{
    Desconhecido = 0,
    GuiaSPSADT = 1,
    PedidoMedico = 2,
    Laudo = 3,
    Receita = 4
}

/// <summary>Status de processamento de um documento individual.</summary>
public enum StatusDocumento
{
    Pendente = 0,
    EnviadoExtend = 1,
    AguardandoExtend = 2,
    Processado = 3,
    Erro = 4
}

/// <summary>Severidade de uma divergência de auditoria.</summary>
public enum SeveridadeDivergencia
{
    Baixa = 1,
    Media = 2,
    Alta = 3,
    Critica = 4
}

/// <summary>Status de uma divergência no fluxo de revisão humana.</summary>
public enum StatusDivergencia
{
    Pendente = 0,
    EmRevisao = 1,
    Aceita = 2,
    Rejeitada = 3,
    CorrecaoSolicitada = 4
}

/// <summary>
/// Tipo de divergência detectada pelas regras de auditoria.
/// A=IntraGuia | B=PedidoVsGuia | C=Confiança | D=OrigemSuspeita | E=Duplicidade | F=AusenciaDocumental
/// </summary>
public enum TipoDivergencia
{
    // Grupo A — Regras intra-guia
    ItemSolicitadoNaoRealizado = 1,
    ItemRealizadoNaoSolicitado = 2,
    QuantidadeDivergente = 3,
    SomaDosItensDivergenteDoTotal = 4,
    TotalProcedimentosIncompativelComTotalGeral = 5,

    // Grupo B — Pedido x Guias
    ItemPedidoNaoEncontradoEmGuia = 10,
    ItemGuiaSemPedido = 11,
    ItemRealizadoSemAutorizacao = 12,
    QuantidadeGuiaMaiorQuePedido = 13,
    CrmDivergente = 14,
    PacienteDivergente = 15,
    DataSuspeitaPedidoGuia = 16,

    // Grupo C — Confiança / Revisão humana
    CampoCriticoReviewScoreBaixo = 20,
    CampoCriticoReviewScoreAlerta = 21,
    CampoCriticoOcrBaixo = 22,
    CampoImportanteOcrAlerta = 23,
    CampoCriticoSemCitacao = 24,
    ItemPedidoBaixaLegibilidade = 25,

    // Grupo D — Origem suspeita
    OrigemSuspeita = 30,

    // Grupo E — Duplicidade
    DuplicidadeNoLote = 40,
    DuplicidadeEntreLotes = 41,

    // Grupo F — Ausência documental
    GuiaSemPedido = 50,
    PedidoSemGuia = 51,

    // Genérico / legado
    CampoObrigatorioAusente = 99,
    ConfiancaBaixa = 100
}

/// <summary>Modo de auditoria para tratamento de origem suspeita (Regra D).</summary>
public enum ModoAuditoria
{
    /// <summary>Só aceita itens ancorados nas páginas primárias do pedido.</summary>
    Estrito = 0,
    /// <summary>Aceita itens de páginas impressas, mas com alerta forte.</summary>
    Assistido = 1
}
