using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Domain.Entities;

public class DivergenciaAuditoria : EntityBase
{
    public int DocumentoId { get; set; }
    public int? AtendimentoAgrupadoId { get; set; }
    public TipoDivergencia Tipo { get; set; }
    public SeveridadeDivergencia Severidade { get; set; }
    public StatusDivergencia Status { get; set; } = StatusDivergencia.Pendente;
    public string Descricao { get; set; } = string.Empty;
    public string? DetalhesTecnicos { get; set; }
    public double? ValorConfianca { get; set; }
    public string? CampoAfetado { get; set; }
    public string? ValorEncontrado { get; set; }
    public string? ValorEsperado { get; set; }

    // Navigation
    public Documento Documento { get; set; } = null!;
    public AtendimentoAgrupado? AtendimentoAgrupado { get; set; }
    public RevisaoHumana? Revisao { get; set; }
}
