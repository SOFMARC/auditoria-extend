using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AuditoriaExtend.Application.Common;

/// <summary>
/// Utilitário centralizado para parsing do JSON extraído pela Extend e
/// comparação robusta de textos. Elimina a repetição de lógica manual
/// espalhada pelo AuditoriaRegraService.
/// </summary>
public static class ExtracaoJsonHelper
{
    // ─────────────────────────────────────────────────────────────────────
    // 1. Extração de campos escalares
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Obtém o valor textual de um campo no JSON normalizado.
    /// Suporta tanto o formato wrapper {value: X, ...} quanto valor direto.
    /// Tenta os nomes na ordem fornecida (camelCase primeiro, snake_case como fallback).
    /// </summary>
    public static string? ObterCampoTexto(JObject? fields, params string[] nomes)
    {
        if (fields == null) return null;
        foreach (var nome in nomes)
        {
            var token = fields[nome];
            if (token == null) continue;
            var val = DesembrulharValor(token);
            if (val != null) return val.ToString()?.Trim();
        }
        return null;
    }

    /// <summary>
    /// Obtém o valor booleano de um campo no JSON normalizado.
    /// </summary>
    public static bool? ObterCampoBoolean(JObject? fields, params string[] nomes)
    {
        if (fields == null) return null;
        foreach (var nome in nomes)
        {
            var token = fields[nome];
            if (token == null) continue;
            var val = DesembrulharValor(token);
            if (val == null) continue;
            if (val.Type == JTokenType.Boolean) return val.Value<bool?>();
            var s = val.ToString().Trim().ToLowerInvariant();
            if (s == "true") return true;
            if (s == "false") return false;
        }
        return null;
    }

    /// <summary>
    /// Obtém o valor numérico de um campo no JSON normalizado.
    /// Suporta: escalar numérico, string numérica, objeto monetário {amount, iso_4217_currency_code}.
    /// </summary>
    public static double? ObterCampoNumerico(JObject? fields, params string[] nomes)
    {
        if (fields == null) return null;
        foreach (var nome in nomes)
        {
            var token = fields[nome];
            if (token == null) continue;
            var val = DesembrulharValor(token);
            if (val == null) continue;
            var result = ExtrairNumeroDeToken(val);
            if (result.HasValue) return result;
        }
        return null;
    }

    /// <summary>
    /// Obtém o valor monetário (amount) de um campo no JSON normalizado.
    /// Campos monetários na Extend têm formato {amount: X, iso_4217_currency_code: "BRL"}.
    /// </summary>
    public static double? ObterCampoMonetario(JObject? fields, params string[] nomes)
    {
        if (fields == null) return null;
        foreach (var nome in nomes)
        {
            var token = fields[nome];
            if (token == null) continue;

            // Formato wrapper {value: {amount: X, ...}, confidence: Y}
            var val = DesembrulharValor(token);
            if (val is JObject obj)
            {
                var amount = obj["amount"];
                if (amount != null)
                {
                    var r = ExtrairNumeroDeToken(amount);
                    if (r.HasValue) return r;
                }
            }
            // Valor direto numérico
            if (val != null)
            {
                var r = ExtrairNumeroDeToken(val);
                if (r.HasValue) return r;
            }
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. Extração de arrays de itens estruturados
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extrai uma lista de <see cref="ItemExtraido"/> de um campo de array no JSON.
    /// Suporta itens_pedido, itens_solicitados e itens_realizados.
    /// </summary>
    public static IReadOnlyList<ItemExtraido> ObterItens(JObject? fields, params string[] nomes)
    {
        if (fields == null) return Array.Empty<ItemExtraido>();
        foreach (var nome in nomes)
        {
            var token = fields[nome];
            if (token == null) continue;

            // Pode estar embrulhado em {value: [...], confidence: ...}
            var val = DesembrulharValor(token);
            if (val is not JArray arr) continue;
            if (arr.Count == 0) continue;

            var lista = new List<ItemExtraido>(arr.Count);
            foreach (var item in arr)
            {
                if (item is not JObject obj) continue;
                lista.Add(ParsearItem(obj));
            }
            return lista;
        }
        return Array.Empty<ItemExtraido>();
    }

    private static ItemExtraido ParsearItem(JObject obj)
    {
        return new ItemExtraido
        {
            Ordem                = obj["ordem"]?.Value<int?>(),
            CodigoProcedimento   = obj["codigo_procedimento"]?.Value<string>()?.Trim(),
            DescricaoOriginal    = obj["descricao_original"]?.Value<string>()?.Trim(),
            DescricaoNormalizada = obj["descricao_normalizada"]?.Value<string>()?.Trim(),
            QuantidadeSolicitada = obj["quantidade_solicitada"]?.Value<double?>()
                                   ?? obj["quantidade"]?.Value<double?>(),
            QuantidadeAutorizada = obj["quantidade_autorizada"]?.Value<double?>(),
            QuantidadeRealizada  = obj["quantidade_realizada"]?.Value<double?>(),
            ValorUnitario        = obj["valor_unitario"]?.Value<double?>(),
            ValorTotal           = obj["valor_total"]?.Value<double?>(),
            Lateralidade         = obj["lateralidade"]?.Value<string>()?.Trim(),
            ObservacaoItem       = obj["observacao_item"]?.Value<string>()?.Trim(),
            Tabela               = obj["tabela"]?.Value<string>()?.Trim(),
            DataExecucao         = obj["data_execucao"]?.Value<string>()?.Trim(),
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. Extração de metadata por campo
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extrai os metadados de todos os campos do JSON normalizado.
    /// Cada campo tem formato: { "value": X, "ocrConfidence": Y, "logprobsConfidence": Z, "citations": [...] }
    /// </summary>
    public static IReadOnlyList<MetadadoCampo> ObterMetadados(JObject? fields)
    {
        if (fields == null) return Array.Empty<MetadadoCampo>();
        var lista = new List<MetadadoCampo>();
        foreach (var prop in fields.Properties())
        {
            if (prop.Value is not JObject wrapper) continue;
            lista.Add(ParsearMetadado(prop.Name, wrapper));
        }
        return lista;
    }

    /// <summary>
    /// Extrai os metadados de um campo específico.
    /// </summary>
    public static MetadadoCampo? ObterMetadadoCampo(JObject? fields, string nomeCampo)
    {
        if (fields == null) return null;
        var token = fields[nomeCampo];
        if (token is not JObject wrapper) return null;
        return ParsearMetadado(nomeCampo, wrapper);
    }

    private static MetadadoCampo ParsearMetadado(string nome, JObject wrapper)
    {
        // Citações: podem ser array de strings ou array de objetos
        var citacoes = new List<string>();
        var citToken = wrapper["citations"] ?? wrapper["citation"];
        if (citToken is JArray citArr)
        {
            foreach (var c in citArr)
                citacoes.Add(c.ToString());
        }
        else if (citToken != null)
        {
            citacoes.Add(citToken.ToString());
        }

        return new MetadadoCampo
        {
            NomeCampo          = nome,
            Value              = wrapper["value"]?.ToString(),
            OcrConfidence      = wrapper["ocrConfidence"]?.Value<double?>()
                                 ?? wrapper["confidence"]?.Value<double?>(),
            LogprobsConfidence = wrapper["logprobsConfidence"]?.Value<double?>(),
            ReviewAgentScore   = wrapper["reviewAgentScore"]?.Value<double?>(),
            Citations          = citacoes,
            PageNumber         = wrapper["pageNumber"]?.Value<int?>(),
        };
    }

    /// <summary>
    /// Calcula a confiança média dos campos, priorizando ocrConfidence.
    /// Ignora campos sem nenhuma métrica de confiança.
    /// </summary>
    public static double CalcularConfidenceMedia(IReadOnlyList<MetadadoCampo> metadados)
    {
        var valores = metadados
            .Select(m => m.ConfidenceEfetiva)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
        return valores.Count > 0 ? valores.Average() : 0.0;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 4. Normalização e comparação de texto
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normaliza um texto para comparação: remove acentos, converte para maiúsculas,
    /// remove caracteres não alfanuméricos e colapsa espaços.
    /// </summary>
    public static string NormalizarTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        // Normaliza unicode para decompor acentos
        var normalizado = texto.Normalize(NormalizationForm.FormD);

        // Remove marcas diacríticas (acentos)
        var sb = new StringBuilder(normalizado.Length);
        foreach (var c in normalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var resultado = sb.ToString()
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormC);

        // Remove caracteres não alfanuméricos exceto espaço
        resultado = Regex.Replace(resultado, @"[^A-Z0-9 ]", " ");

        // Colapsa espaços múltiplos
        resultado = Regex.Replace(resultado, @"\s+", " ").Trim();

        return resultado;
    }

    /// <summary>
    /// Compara dois nomes de forma tolerante a acentos, caixa, espaços extras
    /// e pequenas variações. Usa similaridade por tokens (Jaccard) como fallback.
    /// </summary>
    public static bool NomesSimilares(string? a, string? b, double limiarJaccard = 0.60)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;

        var na = NormalizarTexto(a);
        var nb = NormalizarTexto(b);

        // Igualdade exata após normalização
        if (na == nb) return true;

        // Um contém o outro (abreviações)
        if (na.Contains(nb) || nb.Contains(na)) return true;

        // Similaridade por tokens (Jaccard)
        var tokensA = new HashSet<string>(na.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var tokensB = new HashSet<string>(nb.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var intersecao = tokensA.Intersect(tokensB).Count();
        var uniao = tokensA.Union(tokensB).Count();
        if (uniao == 0) return false;
        var jaccard = (double)intersecao / uniao;
        return jaccard >= limiarJaccard;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 5. Helpers internos
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Desembrulha o wrapper {value: X, ...} retornando o token interno,
    /// ou retorna o próprio token se não for um wrapper.
    /// </summary>
    private static JToken? DesembrulharValor(JToken token)
    {
        if (token.Type == JTokenType.Null) return null;

        // Wrapper {value: X, ...}
        if (token is JObject obj && obj.ContainsKey("value"))
        {
            var inner = obj["value"];
            if (inner == null || inner.Type == JTokenType.Null) return null;
            return inner;
        }

        return token;
    }

    private static double? ExtrairNumeroDeToken(JToken token)
    {
        if (token.Type == JTokenType.Null) return null;

        // Objeto monetário {amount: X, ...}
        if (token is JObject obj)
        {
            var amount = obj["amount"];
            if (amount != null && amount.Type != JTokenType.Null)
                return amount.Value<double?>();
            return null;
        }

        // Numérico direto
        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            return token.Value<double?>();

        // String numérica
        var s = token.ToString().Trim().Replace(",", ".");
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;

        return null;
    }
}
