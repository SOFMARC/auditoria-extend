using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Application.DTOs;

public class DivergenciaAuditoriaDto
{
    public int Id { get; set; }
    public int DocumentoId { get; set; }
    public string NomeArquivoDocumento { get; set; } = string.Empty;
    public int? AtendimentoAgrupadoId { get; set; }
    /// <summary>JSON dos dados extraídos do documento vinculado, para exibir na tela de revisão.</summary>
    public string? DadosExtaidosDocumento { get; set; }
    public string? TipoDocumentoLabel { get; set; }
    public TipoDivergencia Tipo { get; set; }
    public SeveridadeDivergencia Severidade { get; set; }
    public StatusDivergencia Status { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string? DetalhesTecnicos { get; set; }
    public double? ValorConfianca { get; set; }
    public string? CampoAfetado { get; set; }
    public string? ValorEncontrado { get; set; }
    public string? ValorEsperado { get; set; }
    public DateTime DataCriacao { get; set; }

    public string SeveridadeLabel => Severidade switch
    {
        SeveridadeDivergencia.Critica => "Crítica",
        SeveridadeDivergencia.Alta => "Alta",
        SeveridadeDivergencia.Media => "Média",
        SeveridadeDivergencia.Baixa => "Baixa",
        _ => "Desconhecida"
    };

    public string StatusLabel => Status switch
    {
        StatusDivergencia.Pendente => "Pendente",
        StatusDivergencia.EmRevisao => "Em Revisão",
        StatusDivergencia.Aceita => "Aceita",
        StatusDivergencia.Rejeitada => "Rejeitada",
        StatusDivergencia.CorrecaoSolicitada => "Correção Solicitada",
        _ => "Desconhecido"
    };
}

public class RevisarDivergenciaDto
{
    public int DivergenciaId { get; set; }
    public string Decisao { get; set; } = string.Empty; // "Aceitar", "Rejeitar", "Corrigir"
    public string NomeAuditor { get; set; } = string.Empty;
    public string? Justificativa { get; set; }
    public string? ObservacaoCorrecao { get; set; }
}
