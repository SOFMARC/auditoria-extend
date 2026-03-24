using Microsoft.AspNetCore.Mvc;
using AuditoriaExtend.Application.Interfaces;

namespace AuditoriaExtend.Web.Controllers;

/// <summary>
/// Endpoint de webhook para receber callbacks da API Extend.
/// A Extend chama POST /api/webhook/extend quando a extração de um documento é concluída.
/// </summary>
[ApiController]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookProcessorService _webhookProcessor;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IWebhookProcessorService webhookProcessor,
        ILogger<WebhookController> logger)
    {
        _webhookProcessor = webhookProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Recebe o evento de conclusão da extração enviado pela Extend.
    /// Eventos suportados: extract_run.processed, extract_run.failed
    /// </summary>
    [HttpPost("extend")]
    public async Task<IActionResult> ReceberRetornoExtend()
    {
        string payloadJson;

        try
        {
            using var reader = new StreamReader(Request.Body);
            payloadJson = await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook Extend: erro ao ler o corpo da requisição");
            return BadRequest("Erro ao ler o payload.");
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            _logger.LogWarning("Webhook Extend: payload vazio recebido");
            return BadRequest("Payload vazio.");
        }

        _logger.LogInformation("Webhook Extend: payload recebido ({Tamanho} bytes)", payloadJson.Length);

        try
        {
            await _webhookProcessor.ProcessarRetornoExtendAsync(payloadJson);
            return Ok(new { message = "Webhook processado com sucesso." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook Extend: erro ao processar payload");
            // Retorna 200 para evitar que a Extend reenvie o webhook em loop
            return Ok(new { message = "Erro interno ao processar webhook. Verificar logs." });
        }
    }

    /// <summary>
    /// Endpoint de verificação de saúde do webhook.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });
}
