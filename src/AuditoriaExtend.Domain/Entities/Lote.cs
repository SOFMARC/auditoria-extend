using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Domain.Entities;

public class Lote : EntityBase
{
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoArquivo { get; set; } = string.Empty;
    public long TamanhoArquivo { get; set; }
    public StatusLote Status { get; set; } = StatusLote.Pendente;
    public int QuantidadeDocumentos { get; set; }
    public int QuantidadeProcessados { get; set; }
    public int QuantidadeDivergencias { get; set; }
    public string? MensagemErro { get; set; }
    public DateTime? DataInicioProcesamento { get; set; }
    public DateTime? DataFimProcessamento { get; set; }

    // Navigation
    public ICollection<Documento> Documentos { get; set; } = new List<Documento>();
}
