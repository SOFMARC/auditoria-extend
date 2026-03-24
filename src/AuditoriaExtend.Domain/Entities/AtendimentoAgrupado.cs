namespace AuditoriaExtend.Domain.Entities;

public class AtendimentoAgrupado : EntityBase
{
    public int LoteId { get; set; }

    // Identificação do atendimento
    public string? NomePaciente { get; set; }
    public string? NomeMedico { get; set; }
    public string? CrmMedico { get; set; }
    public string? NumeroGuia { get; set; }
    public string? NumeroPedido { get; set; }
    public DateTime? DataAtendimento { get; set; }

    // Contadores
    public int QuantidadeDocumentos { get; set; }
    public int QuantidadeDivergencias { get; set; }
    public int QuantidadeRevisaoHumana { get; set; }

    // Score de risco calculado (Regra G)
    /// <summary>Score de risco de 0 a 100 calculado com base em divergências, confiança e flags.</summary>
    public double ScoreRisco { get; set; }
    public bool RevisaoHumanaNecessaria { get; set; }

    // Navigation
    public Lote Lote { get; set; } = null!;
    public ICollection<Documento> Documentos { get; set; } = new List<Documento>();
    public ICollection<DivergenciaAuditoria> Divergencias { get; set; } = new List<DivergenciaAuditoria>();
}
