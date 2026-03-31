using AuditoriaExtend.Application.Services;
using AuditoriaExtend.Web.Workers;
using Serilog;
using Serilog.Debugging;
using AuditoriaExtend.Infrastructure;
using AuditoriaExtend.Application;

try
{
    var builder = WebApplication.CreateBuilder(args);

    var logDir = @"C:\LogsAppWeb\auditoria";
    Directory.CreateDirectory(logDir);

    var internalSerilogErrors = Path.Combine(logDir, "serilog-selflog.txt");
    SelfLog.Enable(msg =>
    {
        try
        {
            File.AppendAllText(internalSerilogErrors, msg + Environment.NewLine);
        }
        catch
        {
            // evita quebrar startup por causa do próprio selflog
        }
    });

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File(
            path: Path.Combine(logDir, "log-.txt"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 15,
            shared: true,
            outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message}{NewLine}{Exception}")
        .CreateLogger();

    builder.Host.UseSerilog();

    Log.Information("Aplicação iniciada - teste de arquivo");

    builder.Services.AddControllersWithViews();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    // IHttpClientFactory necessário para FraudeAnaliseService chamar a API OpenAI
    builder.Services.AddHttpClient();

    builder.Services.Configure<AuditoriaOptions>(
        builder.Configuration.GetSection(AuditoriaOptions.SectionName));

    // Worker antifraude: verifica lotes concluídos sem divergências pendentes a cada 5 minutos
    builder.Services.AddHostedService<FraudeAnaliseWorker>();

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

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

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

public partial class Program { }
