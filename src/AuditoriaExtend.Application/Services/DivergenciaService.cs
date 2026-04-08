using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Entities;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Domain.Repositories;
using AutoMapper;
using Newtonsoft.Json.Linq;

namespace AuditoriaExtend.Application.Services;

public class DivergenciaService : IDivergenciaService
{
    private readonly IRepository<DivergenciaAuditoria> _repo;
    private readonly IRepository<Documento> _repoDoc;
    private readonly IRepository<RevisaoHumana> _repoRevisao;

    private readonly IMapper _mapper;

    public DivergenciaService(IRepository<DivergenciaAuditoria> repo,
        IRepository<Documento> repoDoc, IMapper mapper)
    {
        _repo = repo;
        _repoDoc = repoDoc;
        _mapper = mapper;
    }

    public async Task AplicarCorrecaoNoJsonAsync(int divergenciaId, string novoValorJson)
    {
        var divergencia = await _repo.GetByIdAsync(divergenciaId);

        if (divergencia == null)
            throw new Exception("Divergência não encontrada.");

        var documento = await _repoDoc.GetByIdAsync(divergencia.DocumentoId);
        if (documento == null)
            throw new Exception("Documento não encontrado.");

        if (string.IsNullOrWhiteSpace(documento.DadosExtraidos))
            throw new Exception("Documento sem DadosExtraidos.");

        if (string.IsNullOrWhiteSpace(divergencia.CampoAfetado))
            throw new Exception("Divergência sem CampoAfetado.");

        var json = JObject.Parse(documento.DadosExtraidos);
        var novoValor = JToken.Parse(novoValorJson);

        AplicarNovoValorNoCampo(json, divergencia.CampoAfetado, novoValor);

        documento.DadosExtraidos = json.ToString(Newtonsoft.Json.Formatting.None);
        documento.DataAtualizacao = DateTime.Now;

        await _repoDoc.UpdateAsync(documento);
        await _repoDoc.SaveChangesAsync();
    }

    private static IEnumerable<DivergenciaAuditoria> RemoverDuplicadas(IEnumerable<DivergenciaAuditoria> divergencias)
    {
        return divergencias
            .GroupBy(d => new
            {
                d.DocumentoId,
                d.Tipo,
                CampoNormalizado = NormalizarCampo(d.CampoAfetado),
                d.ValorEncontrado,
                d.ValorEsperado
            })
            .Select(g => g
                .OrderByDescending(d => TemUnderline(d.CampoAfetado)) // prioriza total_geral
                .ThenByDescending(d => d.DataCriacao)
                .First());
    }

    private static string NormalizarCampo(string? campo)
    {
        if (string.IsNullOrWhiteSpace(campo))
            return string.Empty;

        return ToSnakeCase(campo);
    }

    private static bool TemUnderline(string? campo)
    {
        return !string.IsNullOrWhiteSpace(campo) && campo.Contains('_');
    }

    private static void AplicarNovoValorNoCampo(JObject json, string campoAfetado, JToken novoValor)
    {
        var encontrou = false;

        foreach (var nomeCampo in ObterAliases(campoAfetado))
        {
            if (json[nomeCampo] is JObject campoObj && campoObj["value"] != null)
            {
                campoObj["value"] = novoValor.DeepClone();
                encontrou = true;
            }
        }

        if (!encontrou)
            throw new Exception($"Campo '{campoAfetado}' não encontrado no JSON.");
    }

    private static IEnumerable<string> ObterAliases(string campo)
    {
        yield return campo;

        var snake = ToSnakeCase(campo);
        if (!string.Equals(snake, campo, StringComparison.Ordinal))
            yield return snake;
    }

    private static string ToSnakeCase(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return texto;

        var chars = new List<char>();

        for (int i = 0; i < texto.Length; i++)
        {
            var c = texto[i];

            if (char.IsUpper(c) && i > 0)
                chars.Add('_');

            chars.Add(char.ToLowerInvariant(c));
        }

        return new string(chars.ToArray());
    }

    public async Task CorrigirValorExtraidoAsync(int divergenciaId, string novoValorJson)
    {
        var divergencia = await _repo.GetByIdAsync(divergenciaId);

        if (divergencia == null)
            throw new Exception("Divergência não encontrada.");

        // aqui precisa existir DocumentoId na divergência
        var documento = await _repoDoc.GetByIdAsync(divergencia.DocumentoId);

        if (documento == null)
            throw new Exception("Documento não encontrado.");

        if (string.IsNullOrWhiteSpace(documento.DadosExtraidos))
            throw new Exception("Documento sem dados extraídos.");

        var json = JObject.Parse(documento.DadosExtraidos);
        var novoValor = JToken.Parse(novoValorJson);

        // Atualiza os dois campos, porque no seu JSON existem os dois formatos
        if (json["itensSolicitados"] is JObject itensSolicitados)
            itensSolicitados["value"] = novoValor;

        if (json["itens_solicitados"] is JObject itensSolicitadosSnake)
            itensSolicitadosSnake["value"] = novoValor;

        documento.DadosExtraidos = json.ToString(Newtonsoft.Json.Formatting.None);
        documento.DataAtualizacao = DateTime.Now;

        await _repoDoc.UpdateAsync(documento);
        await _repoDoc.SaveChangesAsync();
    }

    public async Task<DivergenciaAuditoriaDto> CriarAsync(int documentoId, TipoDivergencia tipo,
        SeveridadeDivergencia severidade, string descricao,
        int? atendimentoAgrupadoId = null, double? valorConfianca = null,
        string? campoAfetado = null, string? valorEncontrado = null, string? valorEsperado = null)
    {
        var div = new DivergenciaAuditoria
        {
            DocumentoId = documentoId,
            AtendimentoAgrupadoId = atendimentoAgrupadoId,
            Tipo = tipo,
            Severidade = severidade,
            Status = StatusDivergencia.Pendente,
            Descricao = descricao,
            ValorConfianca = valorConfianca,
            CampoAfetado = campoAfetado,
            ValorEncontrado = NormalizarTextoBanco(valorEncontrado),
            ValorEsperado = NormalizarTextoBanco(valorEsperado),
            DataCriacao = DateTime.UtcNow
        };
        await _repo.AddAsync(div);
        await _repo.SaveChangesAsync();
        return _mapper.Map<DivergenciaAuditoriaDto>(div);
    }

    private static string? NormalizarTextoBanco(string? valor, int max = 500)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return valor;

        var texto = valor.Trim();

        if (texto.StartsWith("{") || texto.StartsWith("["))
        {
            try
            {
                var token = JToken.Parse(texto);
                var resultado = ExtrairValorUtil(token);

                if (string.IsNullOrWhiteSpace(resultado))
                    return "[JSON_COMPLEXO]";

                resultado = resultado.Trim();
                return resultado.Length > max ? resultado.Substring(0, max) : resultado;
            }
            catch
            {
                return "[JSON_INVALIDO]";
            }
        }

        return texto.Length > max ? texto.Substring(0, max) : texto;
    }

    private static string? ExtrairValorUtil(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return null;

        if (token.Type == JTokenType.String ||
            token.Type == JTokenType.Integer ||
            token.Type == JTokenType.Float ||
            token.Type == JTokenType.Boolean)
        {
            return token.ToString();
        }

        if (token is JObject obj)
        {
            // 1. moeda
            if (obj["amount"] != null)
                return obj["amount"]?.ToString();

            // 2. item de procedimento/exame
            if (!string.IsNullOrWhiteSpace(obj["descricao_original"]?.ToString()))
                return obj["descricao_original"]?.ToString();

            if (!string.IsNullOrWhiteSpace(obj["descricao_normalizada"]?.ToString()))
                return obj["descricao_normalizada"]?.ToString();

            if (!string.IsNullOrWhiteSpace(obj["codigo_procedimento"]?.ToString()))
                return obj["codigo_procedimento"]?.ToString();

            // 3. assinatura
            if (obj["is_signed"] != null)
                return obj["is_signed"]?.ToString();

            // 4. nomes úteis
            if (!string.IsNullOrWhiteSpace(obj["nome"]?.ToString()))
                return obj["nome"]?.ToString();

            if (!string.IsNullOrWhiteSpace(obj["printed_name"]?.ToString()))
                return obj["printed_name"]?.ToString();

            return null;
        }

        if (token is JArray arr)
        {
            if (arr.Count == 0)
                return null;

            var valores = arr
                .Select(ExtrairValorUtil)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            if (valores.Count == 0)
                return null;

            return string.Join(" | ", valores);
        }

        return null;
    }

    public async Task<DivergenciaAuditoriaDto?> ObterPorIdAsync(int id)
    {
        var div = await _repo.GetByIdAsync(id);
        if (div == null) return null;
        // Enrich with document data for the Revisar view
        var doc = await _repoDoc.GetByIdAsync(div.DocumentoId);
        if (doc != null) div.Documento = doc;
        var dto = _mapper.Map<DivergenciaAuditoriaDto>(div);
        if (doc != null)
        {
            dto.DadosExtaidosDocumento = doc.DadosExtraidos;
            dto.TipoDocumentoLabel = doc.TipoDocumento switch
            {
                Domain.Enums.TipoDocumento.GuiaSPSADT   => "Guia SPSADT",
                Domain.Enums.TipoDocumento.PedidoMedico => "Pedido Médico",
                Domain.Enums.TipoDocumento.Laudo        => "Laudo",
                Domain.Enums.TipoDocumento.Receita      => "Receita",
                _                                       => "Desconhecido"
            };
            // Converte caminho físico (wwwroot\uploads\...) para URL pública (/uploads/...)
            if (!string.IsNullOrWhiteSpace(doc.CaminhoArquivo))
            {
                var url = doc.CaminhoArquivo
                    .Replace("wwwroot\\", "")
                    .Replace("wwwroot/", "")
                    .Replace("\\", "/");
                dto.UrlDocumentoOriginal = "/" + url.TrimStart('/');
            }
        }
        return dto;
    }

    public async Task<PaginatedList<DivergenciaAuditoriaDto>> ListarAsync(PagedRequest request,
        int? filterStatus = null, int? filterSeveridade = null, int? filterTipo = null, int? loteId = null)
    {
        var todos = await _repo.GetAllAsync();
        var query = todos.AsQueryable();

        if (filterStatus.HasValue)
            query = query.Where(d => (int)d.Status == filterStatus.Value);
        if (filterSeveridade.HasValue)
            query = query.Where(d => (int)d.Severidade == filterSeveridade.Value);
        if (filterTipo.HasValue)
            query = query.Where(d => (int)d.Tipo == filterTipo.Value);

        query = RemoverDuplicadas(query).AsQueryable();

        query = (request.SortBy?.ToLower(), request.SortOrder?.ToLower()) switch
        {
            ("severidade", "asc") => query.OrderBy(d => d.Severidade),
            ("severidade", _) => query.OrderByDescending(d => d.Severidade),
            ("tipo", "asc") => query.OrderBy(d => d.Tipo),
            ("tipo", _) => query.OrderByDescending(d => d.Tipo),
            (_, "asc") => query.OrderBy(d => d.DataCriacao),
            _ => query.OrderByDescending(d => d.DataCriacao)
        };

        var dtos = query.Select(d => _mapper.Map<DivergenciaAuditoriaDto>(d));
        return PaginatedList<DivergenciaAuditoriaDto>.Create(dtos, request.Page, request.PageSize);
    }

    public async Task<IEnumerable<DivergenciaAuditoriaDto>> ListarPorDocumentoAsync(int documentoId)
    {
        var todos = await _repo.GetAllAsync();

        var query = todos
            .Where(d => d.DocumentoId == documentoId && d.Status == StatusDivergencia.Pendente);

        query = RemoverDuplicadas(query);

        return query.Select(d => _mapper.Map<DivergenciaAuditoriaDto>(d));
    }

    public async Task<IEnumerable<DivergenciaAuditoriaDto>> ListarPendentesPorSeveridadeAsync(SeveridadeDivergencia? severidade = null)
    {
        var todos = await _repo.GetAllAsync();

        var query = todos.Where(d => d.Status == StatusDivergencia.Pendente);

        if (severidade.HasValue)
            query = query.Where(d => d.Severidade == severidade.Value);

        query = RemoverDuplicadas(query);

        return query.OrderByDescending(d => d.Severidade)
                    .ThenBy(d => d.DataCriacao)
                    .Select(d => _mapper.Map<DivergenciaAuditoriaDto>(d));
    }

    public async Task AtualizarStatusAsync(int id, StatusDivergencia status)
    {
        var div = await _repo.GetByIdAsync(id);
        if (div == null) return;
        div.Status = status;
        div.DataAtualizacao = DateTime.UtcNow;
        await _repo.UpdateAsync(div);
        await _repo.SaveChangesAsync();
    }

    public async Task<int> ContarPendentesAsync()
    {
        var todos = await _repo.GetAllAsync();
        return todos.Count(d => d.Status == StatusDivergencia.Pendente);
    }
}
