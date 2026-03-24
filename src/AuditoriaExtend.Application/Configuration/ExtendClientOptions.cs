namespace AuditoriaExtend.Application.Configuration;

/// <summary>
/// Opções de configuração para o cliente da API Extend.
/// Definidas na camada Application para evitar dependência circular com Infrastructure.
/// </summary>
public class ExtendClientOptions
{
    public const string SectionName = "Extend";

    /// <summary>ID do extractor configurado para Guia SP/SADT.</summary>
    public string ExtractorIdGuiaSPSADT { get; set; } = string.Empty;

    /// <summary>ID do extractor configurado para Pedido Médico.</summary>
    public string ExtractorIdPedidoMedico { get; set; } = string.Empty;
}
