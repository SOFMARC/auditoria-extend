using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Repositories;
using AuditoriaExtend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AuditoriaExtend.Infrastructure.Repositories;

public class Repository<T> : IRepository<T> where T : class
{
    private readonly AuditoriaDbContext _context;
    private readonly DbSet<T> _set;

    public Repository(AuditoriaDbContext context)
    {
        _context = context;
        _set = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(int id)
    {
        return await _set.FindAsync(id);
    }

    public async Task<IEnumerable<T>> GetAllAsync()
    {
        if (typeof(T) == typeof(DivergenciaAuditoria))
        {
            var itens = await _context.DivergenciasAuditoria
                .AsNoTracking()
                .Select(d => new
                {
                    Divergencia = d,
                    CampoCanonico =
                        d.CampoAfetado == "totalGeral" || d.CampoAfetado == "total_geral" ? "total_geral" :
                        d.CampoAfetado == "numeroCarteira" || d.CampoAfetado == "numero_carteira" ? "numero_carteira" :
                        d.CampoAfetado == "dataSolicitacao" || d.CampoAfetado == "data_solicitacao" ? "data_solicitacao" :
                        d.CampoAfetado == "itensRealizados" || d.CampoAfetado == "itens_realizados" ? "itens_realizados" :
                        d.CampoAfetado == "itensSolicitados" || d.CampoAfetado == "itens_solicitados" ? "itens_solicitados" :
                        d.CampoAfetado == "nomePaciente" || d.CampoAfetado == "nome_paciente" || d.CampoAfetado == "nome_beneficiario" ? "nome_beneficiario" :
                        d.CampoAfetado == "numeroGuia" || d.CampoAfetado == "numero_guia_prestador" ? "numero_guia_prestador" :
                        d.CampoAfetado,
                    Prioridade = d.CampoAfetado != null && d.CampoAfetado.Contains("_") ? 1 : 0
                })
                .GroupBy(x => new
                {
                    x.Divergencia.DocumentoId,
                    x.Divergencia.Tipo,
                    x.Divergencia.Status,
                    x.CampoCanonico,
                    x.Divergencia.ValorEncontrado,
                    x.Divergencia.ValorEsperado
                })
                .Select(g => g
                    .OrderByDescending(x => x.Prioridade)
                    .ThenByDescending(x => x.Divergencia.DataCriacao)
                    .Select(x => x.Divergencia)
                    .First())
                .ToListAsync();

            return itens.Cast<T>();
        }

        return await _set.AsNoTracking().ToListAsync();
    }

    public async Task AddAsync(T entity) => await _set.AddAsync(entity);

    public Task UpdateAsync(T entity)
    {
        var entityType = _context.Model.FindEntityType(typeof(T));
        var primaryKey = entityType?.FindPrimaryKey();

        if (primaryKey != null)
        {
            var keyValues = primaryKey.Properties
                .Select(p => p.PropertyInfo?.GetValue(entity))
                .ToArray();

            var localEntity = _set.Local.FirstOrDefault(e =>
                primaryKey.Properties
                    .Select(p => p.PropertyInfo?.GetValue(e))
                    .SequenceEqual(keyValues));

            if (localEntity != null)
            {
                _context.Entry(localEntity).State = EntityState.Detached;
            }
        }

        _context.Entry(entity).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity)
    {
        _set.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync() => await _context.SaveChangesAsync();
}