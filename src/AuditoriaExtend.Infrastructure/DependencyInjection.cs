using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AuditoriaExtend.Application.Configuration;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Repositories;
using AuditoriaExtend.Infrastructure.Configuration;
using AuditoriaExtend.Infrastructure.Data;
using AuditoriaExtend.Infrastructure.Extend;
using AuditoriaExtend.Infrastructure.Repositories;

namespace AuditoriaExtend.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Entity Framework Core
        services.AddDbContext<AuditoriaDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly(typeof(AuditoriaDbContext).Assembly.FullName)
            )
        );

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // Extend API Client
        services.Configure<ExtendOptions>(configuration.GetSection(ExtendOptions.SectionName));
        services.Configure<ExtendClientOptions>(configuration.GetSection(ExtendClientOptions.SectionName));
        services.AddHttpClient<IExtendClient, ExtendClient>();

        return services;
    }
}
