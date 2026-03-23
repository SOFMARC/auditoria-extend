using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AuditoriaExtend.Domain.Repositories;
using AuditoriaExtend.Infrastructure.Data;
using AuditoriaExtend.Infrastructure.Repositories;

namespace AuditoriaExtend.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuditoriaDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(AuditoriaDbContext).Assembly.FullName)
            )
        );

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        return services;
    }
}
