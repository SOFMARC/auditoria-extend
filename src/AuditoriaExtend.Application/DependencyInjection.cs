using Microsoft.Extensions.DependencyInjection;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Application.Services;

namespace AuditoriaExtend.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(DependencyInjection).Assembly);

        services.AddScoped<ILoteService, LoteService>();
        services.AddScoped<IDocumentoService, DocumentoService>();
        services.AddScoped<IDivergenciaService, DivergenciaService>();
        services.AddScoped<IRevisaoHumanaService, RevisaoHumanaService>();
        services.AddScoped<IImportacaoService, ImportacaoService>();
        services.AddScoped<IAuditoriaRegraService, AuditoriaRegraService>();

        return services;
    }
}
