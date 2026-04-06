namespace AuditoriaExtend.Application.Common;

/// <summary>
/// Representa um item estruturado extraído de um documento pela Extend.
/// Usado tanto para itens_pedido (Pedido Médico) quanto para
/// itens_solicitados / itens_realizados (Guia SPSADT).
/// </summary>
public sealed class ItemExtraido
{
    /// <summary>Ordem de aparição no documento.</summary>
    public int? Ordem { get; init; }

    /// <summary>Código do procedimento (ex: "40301078").</summary>
    public string? CodigoProcedimento { get; init; }

    /// <summary>Descrição exatamente como aparece no documento.</summary>
    public string? DescricaoOriginal { get; init; }

    /// <summary>Descrição normalizada para comparação (ex: "MAMOGRAFIA_BILATERAL_DIGITAL").</summary>
    public string? DescricaoNormalizada { get; init; }

    /// <summary>Quantidade solicitada (pode ser null quando não explícita no pedido).</summary>
    public double? QuantidadeSolicitada { get; init; }

    /// <summary>Quantidade autorizada (guia SPSADT).</summary>
    public double? QuantidadeAutorizada { get; init; }

    /// <summary>Quantidade realizada (guia SPSADT — itens_realizados).</summary>
    public double? QuantidadeRealizada { get; init; }

    /// <summary>Valor unitário do item (guia SPSADT — itens_realizados).</summary>
    public double? ValorUnitario { get; init; }

    /// <summary>Valor total do item (guia SPSADT — itens_realizados).</summary>
    public double? ValorTotal { get; init; }

    /// <summary>Lateralidade quando existir (ex: "BILATERAL", "DIREITA").</summary>
    public string? Lateralidade { get; init; }

    /// <summary>Observação complementar do item.</summary>
    public string? ObservacaoItem { get; init; }

    /// <summary>Tabela do procedimento (ex: "22").</summary>
    public string? Tabela { get; init; }

    /// <summary>Data de execução (apenas itens_realizados).</summary>
    public string? DataExecucao { get; init; }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers de comparação
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retorna a chave de comparação primária para cruzamento entre documentos.
    /// Prioridade: CodigoProcedimento → DescricaoNormalizada → DescricaoOriginal normalizada.
    /// </summary>
    public string ChaveComparacao =>
        !string.IsNullOrWhiteSpace(CodigoProcedimento)
            ? ExtracaoJsonHelper.NormalizarTexto(CodigoProcedimento)
            : !string.IsNullOrWhiteSpace(DescricaoNormalizada)
                ? ExtracaoJsonHelper.NormalizarTexto(DescricaoNormalizada)
                : ExtracaoJsonHelper.NormalizarTexto(DescricaoOriginal ?? string.Empty);

    /// <summary>
    /// Verifica se este item corresponde a outro, usando a estratégia em camadas:
    /// 1. Código de procedimento (quando ambos existem)
    /// 2. Descrição normalizada (quando ambas existem)
    /// 3. Descrição original normalizada (fallback)
    /// </summary>
    public bool Corresponde(ItemExtraido outro)
    {
        // Nível 1: código de procedimento
        if (!string.IsNullOrWhiteSpace(CodigoProcedimento) && !string.IsNullOrWhiteSpace(outro.CodigoProcedimento))
            return ExtracaoJsonHelper.NormalizarTexto(CodigoProcedimento) ==
                   ExtracaoJsonHelper.NormalizarTexto(outro.CodigoProcedimento);

        // Nível 2: descrição normalizada
        if (!string.IsNullOrWhiteSpace(DescricaoNormalizada) && !string.IsNullOrWhiteSpace(outro.DescricaoNormalizada))
            return ExtracaoJsonHelper.NormalizarTexto(DescricaoNormalizada) ==
                   ExtracaoJsonHelper.NormalizarTexto(outro.DescricaoNormalizada);

        // Nível 3: descrição original normalizada (fallback)
        var a = ExtracaoJsonHelper.NormalizarTexto(DescricaoOriginal ?? string.Empty);
        var b = ExtracaoJsonHelper.NormalizarTexto(outro.DescricaoOriginal ?? string.Empty);
        return !string.IsNullOrWhiteSpace(a) && a == b;
    }

    /// <summary>Representação legível para logs e mensagens de divergência.</summary>
    public override string ToString()
    {
        if (!string.IsNullOrWhiteSpace(CodigoProcedimento))
            return $"{CodigoProcedimento} — {DescricaoNormalizada ?? DescricaoOriginal}";
        return DescricaoNormalizada ?? DescricaoOriginal ?? "(item sem descrição)";
    }
}
