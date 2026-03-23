namespace AuditoriaExtend.Application.Interfaces;

public interface IAuditoriaRegraService
{
    /// <summary>Executa todas as regras de auditoria para um documento processado.</summary>
    Task<int> AuditarDocumentoAsync(int documentoId);

    /// <summary>Executa auditoria de duplicidade no lote.</summary>
    Task<int> DetectarDuplicidadesAsync(int loteId);
}
