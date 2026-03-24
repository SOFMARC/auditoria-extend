namespace AuditoriaExtend.Application.Interfaces;

/// <summary>
/// Serviço responsável por processar o retorno do webhook da Extend.
/// É chamado pelo WebhookController após receber e validar o payload.
/// </summary>
public interface IWebhookProcessorService
{
    /// <summary>
    /// Processa o payload bruto JSON recebido do webhook da Extend.
    /// Localiza o documento pelo RunId, persiste os dados extraídos
    /// e dispara todas as regras de auditoria.
    /// </summary>
    Task ProcessarRetornoExtendAsync(string payloadJson);
}
