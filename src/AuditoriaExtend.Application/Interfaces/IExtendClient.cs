namespace AuditoriaExtend.Application.Interfaces;

/// <summary>
/// Interface para o cliente da API Extend.
/// A implementação concreta fica no projeto Infrastructure.
/// Seguindo Clean Architecture: Application define a abstração, Infrastructure implementa.
/// </summary>
public interface IExtendClient
{
    /// <summary>
    /// Faz upload de um arquivo para a Extend via POST /files/upload.
    /// Retorna o fileId gerado pela Extend.
    /// </summary>
    Task<string> UploadFileAsync(Stream fileStream, string fileName, CancellationToken ct = default);

    /// <summary>
    /// Inicia uma extração assíncrona na Extend via POST /extract_runs.
    /// Retorna o runId do job de extração.
    /// </summary>
    Task<string> IniciarExtracaoAsync(string fileId, string extractorId, CancellationToken ct = default);
}

/// <summary>Exceção lançada quando a API Extend retorna erro.</summary>
public class ExtendApiException : Exception
{
    public ExtendApiException(string message) : base(message) { }
    public ExtendApiException(string message, Exception inner) : base(message, inner) { }
}
