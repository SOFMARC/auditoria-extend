namespace AuditoriaExtend.Infrastructure.Configuration;

/// <summary>Configurações da integração com a API Extend.</summary>
public class ExtendOptions
{
    public const string SectionName = "Extend";

    /// <summary>Chave de API para autenticação Bearer na Extend.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>ID do extractor configurado para Guia SP/SADT.</summary>
    public string ExtractorIdGuiaSPSADT { get; set; } = string.Empty;

    /// <summary>ID do extractor configurado para Pedido Médico.</summary>
    public string ExtractorIdPedidoMedico { get; set; } = string.Empty;

    /// <summary>Segredo para verificação de assinatura HMAC dos webhooks da Extend.</summary>
    public string WebhookSigningSecret { get; set; } = string.Empty;

    /// <summary>Modo de auditoria: Estrito ou Assistido (para Regra D - Origem Suspeita).</summary>
    public string ModoAuditoria { get; set; } = "Assistido";
}
