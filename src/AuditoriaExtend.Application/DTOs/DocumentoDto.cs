using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Application.DTOs;

public class DocumentoDto
{
    public int Id { get; set; }
    public int LoteId { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public TipoDocumento TipoDocumento { get; set; }
    public StatusDocumento Status { get; set; }
    public double ConfiancaOcr { get; set; }
    public int? ReviewAgentScore { get; set; }
    public string? DadosExtraidos { get; set; }
    public bool OrigemSuspeita { get; set; }
    public bool RevisaoHumanaNecessaria { get; set; }
    public string? MensagemErro { get; set; }
    public int? AtendimentoAgrupadoId { get; set; }
    public DateTime DataCriacao { get; set; }

    // Labels calculados para a view
    public string TipoLabel => TipoDocumento switch
    {
        TipoDocumento.GuiaSPSADT   => "Guia SPSADT",
        TipoDocumento.PedidoMedico => "Pedido Médico",
        TipoDocumento.Laudo        => "Laudo",
        TipoDocumento.Receita      => "Receita",
        _                          => "Desconhecido"
    };

    public string StatusLabel => Status switch
    {
        StatusDocumento.Pendente         => "Pendente",
        StatusDocumento.EnviadoExtend    => "Enviado",
        StatusDocumento.AguardandoExtend => "Aguardando",
        StatusDocumento.Processado       => "Processado",
        StatusDocumento.Erro             => "Erro",
        _                                => "Desconhecido"
    };
}
