using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Application.DTOs;

public class RevisaoHumanaDto
{
    public int Id { get; set; }
    public int DivergenciaId { get; set; }
    public StatusDivergencia Decisao { get; set; }
    public string NomeAuditor { get; set; } = string.Empty;
    public string? Justificativa { get; set; }
    public string? ObservacaoCorrecao { get; set; }
    public DateTime DataRevisao { get; set; }
    public DivergenciaAuditoriaDto? Divergencia { get; set; }
}

public class EstatisticasRevisaoDto
{
    public int TotalPendentes { get; set; }
    public int TotalRevisados { get; set; }
    public int TotalAceitos { get; set; }
    public int TotalRejeitados { get; set; }
    public int TotalCorrecoesSolicitadas { get; set; }
    public double TaxaAceitacao => TotalRevisados == 0 ? 0 : Math.Round((TotalAceitos * 100.0) / TotalRevisados, 1);
}
