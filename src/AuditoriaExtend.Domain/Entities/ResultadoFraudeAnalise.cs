using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Domain.Entities;

/// <summary>
/// Armazena o resultado da análise antifraude via LLM para um lote de documentos.
/// Um lote só é analisado quando não há mais divergências pendentes de revisão humana.
/// </summary>
public class ResultadoFraudeAnalise : EntityBase
{
    public int LoteId { get; set; }
    public Lote? Lote { get; set; }

    /// <summary>Status da análise: Pendente, Processando, Concluido, Erro</summary>
    public StatusFraudeAnalise Status { get; set; } = StatusFraudeAnalise.Pendente;

    /// <summary>JSON completo retornado pelo LLM (estrutura definida no prompt)</summary>
    public string? ResultadoJson { get; set; }

    /// <summary>Status de auditoria: aprovado | aprovado_com_ressalvas | reter_para_analise_manual | bloquear_para_auditoria</summary>
    public string? StatusAuditoria { get; set; }

    /// <summary>Score de risco de 0 a 100</summary>
    public int? ScoreRisco { get; set; }

    /// <summary>Nível de risco: baixo | moderado | alto | critico</summary>
    public string? NivelRisco { get; set; }

    /// <summary>Resumo textual gerado pelo LLM</summary>
    public string? Resumo { get; set; }

    /// <summary>Recomendação final gerada pelo LLM</summary>
    public string? RecomendacaoFinal { get; set; }

    /// <summary>Quantidade de achados retornados pelo LLM</summary>
    public int QuantidadeAchados { get; set; }

    /// <summary>Mensagem de erro caso a análise tenha falhado</summary>
    public string? MensagemErro { get; set; }

    public DateTime? DataInicio { get; set; }
    public DateTime? DataFim { get; set; }
}
