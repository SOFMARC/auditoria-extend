namespace AuditoriaExtend.Domain.Entities;

public class AtendimentoAgrupado : EntityBase
{
    public int LoteId { get; set; }
    public string? NomePaciente { get; set; }
    public string? NomeMedico { get; set; }
    public string? NumeroGuia { get; set; }
    public string? NumeroPedido { get; set; }
    public int QuantidadeDocumentos { get; set; }
    public int QuantidadeDivergencias { get; set; }
    public double ScoreRisco { get; set; }

    // Navigation
    public Lote Lote { get; set; } = null!;
    public ICollection<Documento> Documentos { get; set; } = new List<Documento>();
    public ICollection<DivergenciaAuditoria> Divergencias { get; set; } = new List<DivergenciaAuditoria>();
}
