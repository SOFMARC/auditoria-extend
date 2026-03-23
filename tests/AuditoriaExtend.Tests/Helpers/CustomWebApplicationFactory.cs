using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AuditoriaExtend.Tests.Helpers;

/// <summary>
/// Factory customizada para testes de integração do ImportacaoController.
/// Substitui o banco de dados real pelo InMemory e permite injetar mocks
/// dos serviços de aplicação para isolar o controller.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Mocks expostos para configuração nos testes
    public Mock<IImportacaoService> ImportacaoServiceMock { get; } = new();
    public Mock<ILoteService> LoteServiceMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove o DbContext real e substitui pelo InMemory
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AuditoriaDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<AuditoriaDbContext>(options =>
                options.UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}"));

            // Substitui os serviços reais pelos mocks
            RemoverServico<IImportacaoService>(services);
            RemoverServico<ILoteService>(services);

            services.AddSingleton(ImportacaoServiceMock.Object);
            services.AddSingleton(LoteServiceMock.Object);
        });
    }

    private static void RemoverServico<T>(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
            services.Remove(descriptor);
    }
}
