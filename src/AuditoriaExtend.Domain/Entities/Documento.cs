using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Domain.Entities;

public class Documento : EntityBase
{
    public int LoteId { get; set; }
    public string NomeArquivo { get; set; } = string.Empty;
    public string CaminhoArquivo { get; set; } = string.Empty;
    public TipoDocumento TipoDocumento { get; set; } = TipoDocumento.Desconhecido;
    public StatusDocumento Status { get; set; } = StatusDocumento.Pendente;

    // --- Integração Extend ---
    /// <summary>ID do arquivo enviado ao Extend via POST /files/upload.</summary>
    public string? ExtendFileId { get; set; }
    /// <summary>ID do job de extração (extract_run) retornado pelo Extend.</summary>
    public string? ExtendRunId { get; set; }
    /// <summary>ID do extractor configurado na Extend para este tipo de documento.</summary>
    public string? ExtractorId { get; set; }

    // --- Dados extraídos ---
    /// <summary>JSON completo retornado pelo webhook da Extend (output.fields).</summary>
    public string? DadosExtraidos { get; set; }
    /// <summary>Confiança média de OCR retornada pela Extend (0.0 a 1.0).</summary>
    public double ConfiancaOcr { get; set; }
    /// <summary>Score de revisão do agente Extend (1-5). Null se não disponível.</summary>
    public int? ReviewAgentScore { get; set; }

    // --- Flags de auditoria ---
    /// <summary>Indica que o documento tem itens ancorados apenas em páginas impressas posteriores.</summary>
    public bool OrigemSuspeita { get; set; }
    /// <summary>Indica que o documento requer revisão humana obrigatória.</summary>
    public bool RevisaoHumanaNecessaria { get; set; }

    public string? MensagemErro { get; set; }
    public int? AtendimentoAgrupadoId { get; set; }

    // Navigation
    public Lote Lote { get; set; } = null!;
    public AtendimentoAgrupado? AtendimentoAgrupado { get; set; }
    public ICollection<DivergenciaAuditoria> Divergencias { get; set; } = new List<DivergenciaAuditoria>();
}
