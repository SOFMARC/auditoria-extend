using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Domain.Entities;

public class RevisaoHumana : EntityBase
{
    public int DivergenciaId { get; set; }
    public StatusDivergencia Decisao { get; set; }
    public string NomeAuditor { get; set; } = string.Empty;
    public string? Justificativa { get; set; }
    public string? ObservacaoCorrecao { get; set; }
    public DateTime DataRevisao { get; set; } = DateTime.UtcNow;

    // Navigation
    public DivergenciaAuditoria Divergencia { get; set; } = null!;
}
