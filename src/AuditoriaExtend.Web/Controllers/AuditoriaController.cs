using Microsoft.AspNetCore.Mvc;
using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.Interfaces;

namespace AuditoriaExtend.Web.Controllers;

public class AuditoriaController : Controller
{
    private readonly IDivergenciaService _divergenciaService;
    private readonly ILoteService _loteService;
    private readonly IRevisaoHumanaService _revisaoService;

    public AuditoriaController(IDivergenciaService divergenciaService,
        ILoteService loteService, IRevisaoHumanaService revisaoService)
    {
        _divergenciaService = divergenciaService;
        _loteService = loteService;
        _revisaoService = revisaoService;
    }

    // GET /Auditoria
    public async Task<IActionResult> Index()
    {
        ViewBag.TotalDivergencias = await _divergenciaService.ContarPendentesAsync();
        ViewBag.TotalLotes = await _loteService.ContarTotalAsync();
        var stats = await _revisaoService.ObterEstatisticasAsync();
        ViewBag.Estatisticas = stats;
        return View();
    }

    // GET /Auditoria/Divergencias
    public async Task<IActionResult> Divergencias(int page = 1, int pageSize = 10,
        string sortBy = "DataCriacao", string sortOrder = "desc",
        int? filterStatus = null, int? filterSeveridade = null, int? filterTipo = null)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize, SortBy = sortBy, SortOrder = sortOrder };
        var resultado = await _divergenciaService.ListarAsync(request, filterStatus, filterSeveridade, filterTipo);
        ViewBag.FilterStatus = filterStatus?.ToString() ?? "";
        ViewBag.FilterSeveridade = filterSeveridade?.ToString() ?? "";
        ViewBag.FilterTipo = filterTipo?.ToString() ?? "";
        return View(resultado);
    }

    // GET /Auditoria/Relatorio
    public async Task<IActionResult> Relatorio(int? filterStatus = null, int? filterSeveridade = null)
    {
        var request = new PagedRequest { Page = 1, PageSize = 10000, SortBy = "DataCriacao", SortOrder = "desc" };
        var resultado = await _divergenciaService.ListarAsync(request, filterStatus, filterSeveridade);

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("ID;DocumentoId;Tipo;Severidade;Status;Descricao;DataCriacao");
        foreach (var d in resultado.Items)
        {
            csv.AppendLine($"{d.Id};{d.DocumentoId};{d.Tipo};{d.SeveridadeLabel};{d.StatusLabel};\"{d.Descricao}\";{d.DataCriacao:dd/MM/yyyy HH:mm}");
        }

        return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv", $"divergencias_{DateTime.Now:yyyyMMdd}.csv");
    }
}
