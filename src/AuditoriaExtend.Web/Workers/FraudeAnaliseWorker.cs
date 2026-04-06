using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;
using AuditoriaExtend.Domain.Entities;

namespace AuditoriaExtend.Web.Workers;

/// <summary>
/// Worker em background que verifica periodicamente lotes concluídos sem divergências
/// pendentes e dispara a análise antifraude via LLM para cada um deles.
/// 
/// Intervalo padrão: 5 minutos.
/// O worker usa IServiceScopeFactory para resolver serviços Scoped dentro de um Singleton.
/// </summary>
public class FraudeAnaliseWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FraudeAnaliseWorker> _logger;

    // Intervalo entre verificações (configurável via construtor se necessário)
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);

    public FraudeAnaliseWorker(IServiceScopeFactory scopeFactory, ILogger<FraudeAnaliseWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FraudeAnaliseWorker iniciado. Verificando a cada {Intervalo} minutos.", Intervalo.TotalMinutes);

        // Aguarda 30 segundos na inicialização para o app estar completamente pronto
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessarLotesPendentesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FraudeAnaliseWorker: erro inesperado no ciclo de verificação.");
            }

            await Task.Delay(Intervalo, stoppingToken);
        }

        _logger.LogInformation("FraudeAnaliseWorker encerrado.");
    }

    private async Task ProcessarLotesPendentesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repoLote = scope.ServiceProvider.GetRequiredService<IRepository<Lote>>();
        var fraudeService = scope.ServiceProvider.GetRequiredService<IFraudeAnaliseService>();

        // Busca todos os lotes com status Concluido
        var lotesConc = (await repoLote.GetAllAsync())
            .Where(l => l.Status == StatusLote.Concluido)
            .ToList();

        if (!lotesConc.Any()) return;

        _logger.LogDebug("FraudeAnaliseWorker: verificando {Count} lote(s) concluído(s).", lotesConc.Count);

        foreach (var lote in lotesConc)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var elegivel = await fraudeService.LoteElegivelParaAnaliseAsync(lote.Id);
                if (!elegivel) continue;

                _logger.LogInformation("FraudeAnaliseWorker: lote {LoteId} elegível para análise antifraude. Iniciando...", lote.Id);
                await fraudeService.AnalisarLoteAsync(lote.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FraudeAnaliseWorker: erro ao processar lote {LoteId}.", lote.Id);
            }
        }
    }
}
