namespace AuditoriaExtend.Application.Interfaces;

/// <summary>
/// Serviço de regras de auditoria. Todas as regras são executadas APÓS o retorno da Extend.
/// A=IntraGuia | B=PedidoVsGuia | C=Confiança | D=OrigemSuspeita | E=Duplicidade | F=AusenciaDocumental | G=ScoreRisco
/// </summary>
public interface IAuditoriaRegraService
{
    /// <summary>
    /// Regras C e D por documento: verifica campos críticos com baixa confiança OCR/reviewAgentScore
    /// e detecta origem suspeita (itens em páginas impressas posteriores).
    /// Chamado pelo WebhookProcessorService após cada retorno da Extend.
    /// </summary>
    Task<int> AuditarDocumentoAsync(int documentoId);

    /// <summary>
    /// Regras intra-guia (Grupo A): verifica itens solicitados x realizados, quantidades e totais.
    /// Executado para documentos do tipo GuiaSPSADT.
    /// </summary>
    Task<int> AuditarIntraGuiaAsync(int documentoId);

    /// <summary>
    /// Regras Pedido x Guias (Grupo B): verifica itens do pedido não encontrados em guias,
    /// CRM divergente, paciente divergente e datas suspeitas.
    /// Executado por atendimento agrupado.
    /// </summary>
    Task<int> AuditarPedidoVsGuiasAsync(int atendimentoAgrupadoId);

    /// <summary>
    /// Regra E: detecta duplicidade de exames no lote e entre lotes próximos.
    /// Executado após todos os documentos do lote serem processados.
    /// </summary>
    Task<int> DetectarDuplicidadesAsync(int loteId);

    /// <summary>
    /// Regra F: detecta ausência documental (guia sem pedido / pedido sem guia).
    /// Executado após todos os documentos do lote serem processados.
    /// </summary>
    Task<int> DetectarAusenciaDocumentalAsync(int loteId);

    /// <summary>
    /// Regra G: calcula score de risco (0-100) por atendimento agrupado.
    /// Baseado em: quantidade/severidade de divergências, confiança, origem suspeita, duplicidade.
    /// </summary>
    Task CalcularScoreRiscoAsync(int loteId);
}
