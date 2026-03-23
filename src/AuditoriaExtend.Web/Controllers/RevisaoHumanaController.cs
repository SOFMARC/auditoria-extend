using Microsoft.AspNetCore.Mvc;
using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Web.Controllers;

public class RevisaoHumanaController : Controller
{
    private readonly IRevisaoHumanaService _revisaoService;
    private readonly IDivergenciaService _divergenciaService;

    public RevisaoHumanaController(IRevisaoHumanaService revisaoService, IDivergenciaService divergenciaService)
    {
        _revisaoService = revisaoService;
        _divergenciaService = divergenciaService;
    }

    // GET /RevisaoHumana
    public async Task<IActionResult> Index()
    {
        var stats = await _revisaoService.ObterEstatisticasAsync();
        return View(stats);
    }

    // GET /RevisaoHumana/Fila
    public async Task<IActionResult> Fila(int page = 1, int pageSize = 10,
        string sortBy = "Severidade", string sortOrder = "desc",
        int? filterSeveridade = null)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize, SortBy = sortBy, SortOrder = sortOrder };
        SeveridadeDivergencia? sev = filterSeveridade.HasValue ? (SeveridadeDivergencia)filterSeveridade.Value : null;
        var resultado = await _revisaoService.ObterFilaAsync(request, sev);
        ViewBag.FilterSeveridade = filterSeveridade?.ToString() ?? "";
        return View(resultado);
    }

    // GET /RevisaoHumana/Revisar/{id}
    public async Task<IActionResult> Revisar(int id)
    {
        var divergencia = await _divergenciaService.ObterPorIdAsync(id);
        if (divergencia == null) return NotFound();
        return View(divergencia);
    }

    // POST /RevisaoHumana/Aceitar
    [HttpPost]
    public async Task<IActionResult> Aceitar(int divergenciaId, string nomeAuditor, string? justificativa)
    {
        await _revisaoService.RevisarAsync(new RevisarDivergenciaDto
        {
            DivergenciaId = divergenciaId,
            Decisao = "aceitar",
            NomeAuditor = nomeAuditor,
            Justificativa = justificativa
        });
        TempData["Sucesso"] = "Divergência aceita com sucesso.";
        return RedirectToAction(nameof(Fila));
    }

    // POST /RevisaoHumana/Rejeitar
    [HttpPost]
    public async Task<IActionResult> Rejeitar(int divergenciaId, string nomeAuditor, string justificativa)
    {
        await _revisaoService.RevisarAsync(new RevisarDivergenciaDto
        {
            DivergenciaId = divergenciaId,
            Decisao = "rejeitar",
            NomeAuditor = nomeAuditor,
            Justificativa = justificativa
        });
        TempData["Sucesso"] = "Divergência rejeitada com sucesso.";
        return RedirectToAction(nameof(Fila));
    }

    // POST /RevisaoHumana/SolicitarCorrecao
    [HttpPost]
    public async Task<IActionResult> SolicitarCorrecao(int divergenciaId, string nomeAuditor,
        string? justificativa, string observacaoCorrecao)
    {
        await _revisaoService.RevisarAsync(new RevisarDivergenciaDto
        {
            DivergenciaId = divergenciaId,
            Decisao = "corrigir",
            NomeAuditor = nomeAuditor,
            Justificativa = justificativa,
            ObservacaoCorrecao = observacaoCorrecao
        });
        TempData["Sucesso"] = "Correção solicitada com sucesso.";
        return RedirectToAction(nameof(Fila));
    }

    // GET /RevisaoHumana/Historico
    public async Task<IActionResult> Historico(int page = 1, int pageSize = 10,
        string sortBy = "DataRevisao", string sortOrder = "desc")
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize, SortBy = sortBy, SortOrder = sortOrder };
        var resultado = await _revisaoService.ListarHistoricoAsync(request);
        return View(resultado);
    }

    // GET /RevisaoHumana/Proxima (AJAX)
    [HttpGet]
    public async Task<IActionResult> Proxima()
    {
        var proxima = await _revisaoService.ObterProximaParaRevisaoAsync();
        if (proxima == null) return Json(new { encontrada = false });
        return Json(new { encontrada = true, id = proxima.Id });
    }

    // GET /RevisaoHumana/Relatorio
    public async Task<IActionResult> Relatorio()
    {
        var request = new PagedRequest { Page = 1, PageSize = 10000, SortBy = "DataRevisao", SortOrder = "desc" };
        var historico = await _revisaoService.ListarHistoricoAsync(request);

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("ID;DivergenciaId;Decisao;Auditor;Justificativa;DataRevisao");
        foreach (var r in historico.Items)
        {
            csv.AppendLine($"{r.Id};{r.DivergenciaId};{r.Decisao};{r.NomeAuditor};\"{r.Justificativa}\";{r.DataRevisao:dd/MM/yyyy HH:mm}");
        }

        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv", $"revisoes_{DateTime.Now:yyyyMMdd}.csv");
    }
}
