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
    public string? MensagemErro { get; set; }
    public int? AtendimentoAgrupadoId { get; set; }
    public DateTime DataCriacao { get; set; }
}
