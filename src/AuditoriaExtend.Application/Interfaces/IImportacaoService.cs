using AuditoriaExtend.Application.DTOs;

namespace AuditoriaExtend.Application.Interfaces;

public interface IImportacaoService
{
    /// <summary>Salva o arquivo ZIP no disco e cria o registro do Lote.</summary>
    Task<LoteDto> ReceberArquivoAsync(Stream stream, string nomeArquivo, long tamanho);

    /// <summary>Extrai o ZIP, classifica documentos e dispara auditoria.</summary>
    Task ProcessarLoteAsync(int loteId);
}
