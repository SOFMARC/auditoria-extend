using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Tests.Helpers;

/// <summary>
/// Builder fluente para criação de LoteDto em testes.
/// Facilita a construção de objetos de teste sem repetição de código.
/// </summary>
public class LoteBuilder
{
    private int _id = 1;
    private string _nomeArquivo = "lote_teste.zip";
    private long _tamanhoArquivo = 1024 * 1024; // 1 MB
    private StatusLote _status = StatusLote.Pendente;
    private int _quantidadeDocumentos = 0;
    private int _quantidadeProcessados = 0;
    private string? _mensagemErro = null;
    private DateTime _dataCriacao = DateTime.UtcNow;

    public LoteBuilder ComId(int id) { _id = id; return this; }
    public LoteBuilder ComNome(string nome) { _nomeArquivo = nome; return this; }
    public LoteBuilder ComTamanho(long tamanho) { _tamanhoArquivo = tamanho; return this; }
    public LoteBuilder ComStatus(StatusLote status) { _status = status; return this; }
    public LoteBuilder ComDocumentos(int total, int processados = 0) { _quantidadeDocumentos = total; _quantidadeProcessados = processados; return this; }
    public LoteBuilder ComErro(string mensagem) { _status = StatusLote.Erro; _mensagemErro = mensagem; return this; }

    public LoteDto Build() => new LoteDto
    {
        Id = _id,
        NomeArquivo = _nomeArquivo,
        TamanhoArquivo = _tamanhoArquivo,
        Status = _status,
        QuantidadeDocumentos = _quantidadeDocumentos,
        QuantidadeProcessados = _quantidadeProcessados,
        MensagemErro = _mensagemErro,
        DataCriacao = _dataCriacao
    };

    public static LoteDto Padrao() => new LoteBuilder().Build();
    public static LoteDto Concluido() => new LoteBuilder().ComStatus(StatusLote.Concluido).ComDocumentos(5, 5).Build();
    public static LoteDto ComErro() => new LoteBuilder().ComErro("ZIP corrompido").Build();
}
