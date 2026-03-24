using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Application.DTOs;

public class LoteDto
{
    public int Id { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoArquivo { get; set; } = string.Empty;
    public long TamanhoArquivo { get; set; }
    public StatusLote Status { get; set; }
    public int QuantidadeDocumentos { get; set; }
    public int QuantidadeProcessados { get; set; }
    public int QuantidadeDivergencias { get; set; }
    public string? MensagemErro { get; set; }
    public DateTime DataCriacao { get; set; }
    public DateTime? DataFimProcessamento { get; set; }

    public int PercentualProcessado =>
        QuantidadeDocumentos == 0 ? 0 : (int)((QuantidadeProcessados * 100.0) / QuantidadeDocumentos);
}

public class CriarLoteDto
{
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoArquivo { get; set; } = string.Empty;
    public long TamanhoArquivo { get; set; }
}
