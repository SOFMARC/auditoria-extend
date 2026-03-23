using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Domain.Entities;

public class Documento : EntityBase
{
    public int LoteId { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoArquivo { get; set; } = string.Empty;
    public TipoDocumento TipoDocumento { get; set; } = TipoDocumento.Desconhecido;
    public StatusDocumento Status { get; set; } = StatusDocumento.Pendente;
    public double ConfiancaOcr { get; set; }
    public string? ExtractorId { get; set; }
    public string? DadosExtraidos { get; set; } // JSON raw da Extend
    public string? MensagemErro { get; set; }
    public int? AtendimentoAgrupadoId { get; set; }

    // Navigation
    public Lote Lote { get; set; } = null!;
    public AtendimentoAgrupado? AtendimentoAgrupado { get; set; }
    public ICollection<DivergenciaAuditoria> Divergencias { get; set; } = new List<DivergenciaAuditoria>();
}
