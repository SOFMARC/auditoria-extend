namespace AuditoriaExtend.Domain.Entities;

public class AtendimentoAgrupado : EntityBase
{
    public int LoteId { get; set; }

    /// <summary>Chave de agrupamento: numero_carteira do paciente (ou CPF, ou nome como fallback).</summary>
    public string? NumeroCarteira { get; set; }

    // Identificacao do atendimento
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

    /// <summary>Score de risco de 0 a 100 calculado com base em divergencias, confianca e flags.</summary>
    public double ScoreRisco { get; set; }
    public bool RevisaoHumanaNecessaria { get; set; }

    // Navigation
    public Lote Lote { get; set; } = null!;
    public ICollection<Documento> Documentos { get; set; } = new List<Documento>();
    public ICollection<DivergenciaAuditoria> Divergencias { get; set; } = new List<DivergenciaAuditoria>();
}
