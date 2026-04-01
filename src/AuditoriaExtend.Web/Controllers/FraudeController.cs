using Microsoft.AspNetCore.Mvc;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Domain.Enums;

namespace AuditoriaExtend.Web.Controllers;

public class FraudeController : Controller
{
    private readonly IFraudeAnaliseService _fraudeService;
    private readonly ILoteService _loteService;
    private readonly IDivergenciaService _divergenciaService;

    public FraudeController(
        IFraudeAnaliseService fraudeService,
        ILoteService loteService,
        IDivergenciaService divergenciaService)
    {
        _fraudeService = fraudeService;
        _loteService = loteService;
        _divergenciaService = divergenciaService;
    }

    // GET /Fraude
    // Dashboard: lista todos os lotes com seus resultados de análise antifraude
    public async Task<IActionResult> Index()
    {
        var lotes = await _loteService.ListarRecentesAsync(100);
        var resultados = new List<(Application.DTOs.LoteDto Lote, Domain.Entities.ResultadoFraudeAnalise? Resultado, bool Elegivel)>();

        foreach (var lote in lotes)
        {
            var resultado = await _fraudeService.ObterResultadoAsync(lote.Id);
            var elegivel = resultado == null && await _fraudeService.LoteElegivelParaAnaliseAsync(lote.Id);
            resultados.Add((lote, resultado, elegivel));
        }

        return View(resultados);
    }

    // GET /Fraude/Detalhes/{loteId}
    // Exibe o resultado completo da análise antifraude de um lote
    public async Task<IActionResult> Detalhes(int id)
    {
        var lote = await _loteService.ObterPorIdAsync(id);
        if (lote == null)
        {
            TempData["Erro"] = "Lote não encontrado.";
            return RedirectToAction(nameof(Index));
        }

        var resultado = await _fraudeService.ObterResultadoAsync(id);
        ViewBag.Lote = lote;
        return View(resultado);
    }

    // POST /Fraude/AnalisarAgora/{loteId}
    // Dispara análise manual imediata de um lote elegível
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnalisarAgora(int id)
    {
        var elegivel = await _fraudeService.LoteElegivelParaAnaliseAsync(id);
        if (!elegivel)
        {
            TempData["Erro"] = "Este lote não está elegível para análise antifraude. Verifique se há divergências pendentes ou se a análise já foi realizada.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _fraudeService.AnalisarLoteAsync(id);
            TempData["Sucesso"] = $"Análise antifraude do lote #{id} concluída com sucesso.";
        }
        catch (Exception ex)
        {
            TempData["Erro"] = $"Erro ao executar análise: {ex.Message}";
        }

        return RedirectToAction(nameof(Detalhes), new { id });
    }
}
