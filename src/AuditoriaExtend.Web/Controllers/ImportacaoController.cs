using Microsoft.AspNetCore.Mvc;
using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Application.Interfaces;

namespace AuditoriaExtend.Web.Controllers;

public class ImportacaoController : Controller
{
    private readonly IImportacaoService _importacaoService;
    private readonly ILoteService _loteService;
    private readonly IDocumentoService _documentoService;
    private readonly IDivergenciaService _divergenciaService;
    private const long MaxFileSizeBytes = 100 * 1024 * 1024; // 100 MB

    public ImportacaoController(IImportacaoService importacaoService, ILoteService loteService,
        IDocumentoService documentoService, IDivergenciaService divergenciaService)
    {
        _importacaoService = importacaoService;
        _loteService = loteService;
        _documentoService = documentoService;
        _divergenciaService = divergenciaService;
    }

    // GET /Importacao
    public async Task<IActionResult> Index()
    {
        ViewBag.TotalLotes = await _loteService.ContarTotalAsync();
        ViewBag.MaxFileSize = "100 MB";
        ViewBag.LotesRecentes = (await _loteService.ListarRecentesAsync(5)).ToList();
        return View();
    }

    // GET /Importacao/Upload
    public IActionResult Upload()
    {
        ViewBag.MaxFileSize = "100 MB";
        return View();
    }

    // POST /Importacao/Upload
    [HttpPost]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile arquivo)
    {
        if (arquivo == null || arquivo.Length == 0)
        {
            ModelState.AddModelError("arquivo", "Selecione um arquivo ZIP.");
            ViewBag.MaxFileSize = "100 MB";
            return View();
        }

        if (!arquivo.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("arquivo", "Somente arquivos .ZIP são aceitos.");
            ViewBag.MaxFileSize = "100 MB";
            return View();
        }

        if (arquivo.Length > MaxFileSizeBytes)
        {
            ModelState.AddModelError("arquivo", "O arquivo excede o tamanho máximo de 100 MB.");
            ViewBag.MaxFileSize = "100 MB";
            return View();
        }

        await using var stream = arquivo.OpenReadStream();
        var lote = await _importacaoService.ReceberArquivoAsync(stream, arquivo.FileName, arquivo.Length);

        TempData["Sucesso"] = $"Arquivo '{arquivo.FileName}' recebido com sucesso. Lote #{lote.Id} criado.";
        return RedirectToAction(nameof(Detalhes), new { id = lote.Id });
    }

    // GET /Importacao/Historico
    public async Task<IActionResult> Historico(int page = 1, int pageSize = 10,
        string sortBy = "DataCriacao", string sortOrder = "desc",
        int? filterStatus = null)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize, SortBy = sortBy, SortOrder = sortOrder };
        var resultado = await _loteService.ListarAsync(request, filterStatus);
        ViewBag.FilterStatus = filterStatus?.ToString() ?? "";
        return View(resultado);
    }

    // GET /Importacao/Detalhes/{id}
    public async Task<IActionResult> Detalhes(int id)
    {
        var lote = await _loteService.ObterPorIdAsync(id);
        if (lote == null) return NotFound();

        // Carrega todos os documentos do lote e suas divergências para exibir na tabela
        var documentos = (await _documentoService.ListarPorLoteAsync(id)).ToList();

        // Para cada documento, busca as divergências associadas
        var divergenciasPorDoc = new Dictionary<int, List<AuditoriaExtend.Application.DTOs.DivergenciaAuditoriaDto>>();
        foreach (var doc in documentos)
        {
            var divs = (await _divergenciaService.ListarPorDocumentoAsync(doc.Id)).ToList();
            divergenciasPorDoc[doc.Id] = divs;
        }

        ViewBag.Documentos = documentos;
        ViewBag.DivergenciasPorDoc = divergenciasPorDoc;
        return View(lote);
    }

    // POST /Importacao/Processar/{id}
    [HttpPost]
    public async Task<IActionResult> Processar(int id)
    {
        try
        {
            await _importacaoService.ProcessarLoteAsync(id);
            TempData["Sucesso"] = $"Lote #{id} processado com sucesso.";
        }
        catch (Exception ex)
        {
            TempData["Erro"] = $"Erro ao processar lote: {ex.Message}";
        }
        return RedirectToAction(nameof(Detalhes), new { id });
    }

    // POST /Importacao/Deletar/{id}
    [HttpPost]
    public async Task<IActionResult> Deletar(int id)
    {
        await _loteService.DeletarAsync(id);
        TempData["Sucesso"] = $"Lote #{id} removido com sucesso.";
        return RedirectToAction(nameof(Historico));
    }
}
