using Microsoft.EntityFrameworkCore;
using AuditoriaExtend.Domain.Repositories;
using AuditoriaExtend.Infrastructure.Data;

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

    public async Task<T?> GetByIdAsync(int id) => await _set.FindAsync(id);

    public async Task<IEnumerable<T>> GetAllAsync() => await _set.ToListAsync();

    public async Task AddAsync(T entity) => await _set.AddAsync(entity);

    public Task UpdateAsync(T entity)
    {
        _set.Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity)
    {
        _set.Remove(entity);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync() => await _context.SaveChangesAsync();
}
