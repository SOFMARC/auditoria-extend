using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Application.Interfaces;

public interface IDocumentoService
{
    Task<DocumentoDto> CriarAsync(int loteId, string nomeArquivo, string caminhoArquivo);
    Task<DocumentoDto?> ObterPorIdAsync(int id);
    Task<IEnumerable<DocumentoDto>> ListarPorLoteAsync(int loteId);
    Task AtualizarTipoAsync(int id, TipoDocumento tipo);
    Task AtualizarStatusAsync(int id, StatusDocumento status, string? mensagemErro = null);
    Task SalvarDadosExtracaoAsync(int id, string dadosJson, double confianca, string extractorId);
    Task VincularAtendimentoAsync(int id, int atendimentoAgrupadoId);
}
