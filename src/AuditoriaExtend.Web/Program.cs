using Serilog;
using AuditoriaExtend.Infrastructure;
using AuditoriaExtend.Application;
using AuditoriaExtend.Application.Services;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/auditoria-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    builder.Services.AddControllersWithViews();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    // Configuração das regras de auditoria (modo Assistido por padrão)
    builder.Services.Configure<AuditoriaOptions>(
        builder.Configuration.GetSection(AuditoriaOptions.SectionName));

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthorization();

    // Rota padrão MVC
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    // Rota da API de webhook (recebe callbacks da Extend)
    app.MapControllerRoute(
        name: "api",
        pattern: "api/{controller}/{action=Index}/{id?}");

    Log.Information("Aplicação iniciada com sucesso");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Aplicação encerrada com erro fatal");
}
finally
{
    Log.CloseAndFlush();
}

// Expõe a classe Program para os testes de integração (WebApplicationFactory)
public partial class Program { }
