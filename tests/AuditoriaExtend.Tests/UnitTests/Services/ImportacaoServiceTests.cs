using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AuditoriaExtend.Application.Configuration;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Application.Interfaces;
using AuditoriaExtend.Application.Services;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace AuditoriaExtend.Tests.UnitTests.Services;

/// <summary>
/// Testes unitários para ImportacaoService.
/// Valida o fluxo de importação: receber ZIP → extrair → classificar → enviar para Extend.
/// As regras de auditoria (A-G) NÃO são executadas aqui — ocorrem no WebhookProcessorService.
/// </summary>
public class ImportacaoServiceTests : IDisposable
{
    // ── Mocks das dependências ──────────────────────────────────────────────
    private readonly Mock<ILoteService> _loteServiceMock;
    private readonly Mock<IDocumentoService> _documentoServiceMock;
    private readonly Mock<IExtendClient> _extendClientMock;

    // ── Sistema sob teste ───────────────────────────────────────────────────
    private readonly ImportacaoService _sut;

    // ── Diretório temporário para arquivos físicos ──────────────────────────
    private readonly string _tempDir;

    // ── Opções de configuração ──────────────────────────────────────────────
    private static readonly ExtendClientOptions ExtendOpts = new()
    {
        ExtractorIdGuiaSPSADT   = "ext-guia-001",
        ExtractorIdPedidoMedico = "ext-pedido-001"
    };

    public ImportacaoServiceTests()
    {
        _loteServiceMock    = new Mock<ILoteService>();
        _documentoServiceMock = new Mock<IDocumentoService>();
        _extendClientMock   = new Mock<IExtendClient>();

        _sut = new ImportacaoService(
            _loteServiceMock.Object,
            _documentoServiceMock.Object,
            _extendClientMock.Object,
            Options.Create(ExtendOpts),
            NullLogger<ImportacaoService>.Instance
        );

        _tempDir = Path.Combine(Path.GetTempPath(), $"auditoria_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TU-IMP-01: Upload de arquivo ZIP válido
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ReceberArquivoAsync_ArquivoZipValido_CriaLoteComStatusPendente()
    {
        // Arrange
        var nomeArquivo = "lote_jan_2025.zip";
        var tamanho = 2 * 1024 * 1024L;
        var stream = ZipHelper.CriarZipValido("guia_001.pdf", "pedido_001.pdf");
        var loteEsperado = new LoteBuilder().ComNome(nomeArquivo).ComStatus(StatusLote.Pendente).Build();

        _loteServiceMock
            .Setup(s => s.CriarLoteAsync(It.IsAny<CriarLoteDto>()))
            .ReturnsAsync(loteEsperado);

        // Act
        var resultado = await _sut.ReceberArquivoAsync(stream, nomeArquivo, tamanho);

        // Assert
        resultado.Should().NotBeNull("um LoteDto deve ser retornado após receber o arquivo");
        resultado.Status.Should().Be(StatusLote.Pendente, "o lote recém-criado deve estar com status Pendente");
        resultado.NomeArquivo.Should().Be(nomeArquivo);

        _loteServiceMock.Verify(
            s => s.CriarLoteAsync(It.Is<CriarLoteDto>(dto =>
                dto.NomeArquivo == nomeArquivo &&
                dto.TamanhoArquivo == tamanho)),
            Times.Once,
            "CriarLoteAsync deve ser chamado exatamente uma vez com os dados corretos");
    }

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ReceberArquivoAsync_ArquivoValido_PersisteCaminhoNoDto()
    {
        // Arrange
        var nomeArquivo = "lote_fev_2025.zip";
        var stream = ZipHelper.CriarZipValido("doc.pdf");
        var loteRetornado = new LoteBuilder().ComNome(nomeArquivo).Build();

        _loteServiceMock
            .Setup(s => s.CriarLoteAsync(It.IsAny<CriarLoteDto>()))
            .ReturnsAsync(loteRetornado);

        // Act
        var resultado = await _sut.ReceberArquivoAsync(stream, nomeArquivo, 1024);

        // Assert
        _loteServiceMock.Verify(
            s => s.CriarLoteAsync(It.Is<CriarLoteDto>(dto =>
                !string.IsNullOrEmpty(dto.CaminhoArquivo))),
            Times.Once,
            "O caminho do arquivo deve ser preenchido ao criar o lote");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TU-IMP-04: Processamento de lote — envio para Extend
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_LoteValido_AtualizaStatusProcessandoEAguardandoExtend()
    {
        // Arrange
        var loteId = 42;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "guia_sp_001.pdf", "pedido_001.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).ComCaminho(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarEnviadosExtendAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DocumentoDto { Id = 1, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.SalvarExtendRunIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusDocumento>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _extendClientMock.Setup(e => e.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("file-id-001");
        _extendClientMock.Setup(e => e.IniciarExtracaoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("run-id-001");

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — sequência: Processando → AguardandoExtend
        _loteServiceMock.Verify(
            s => s.AtualizarStatusAsync(loteId, StatusLote.Processando, null),
            Times.Once,
            "O status deve ser atualizado para Processando no início");

        _loteServiceMock.Verify(
            s => s.AtualizarStatusAsync(loteId, StatusLote.AguardandoExtend, null),
            Times.Once,
            "O status deve ser atualizado para AguardandoExtend após envio para Extend");
    }

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_LoteValido_EnviaArquivosParaExtendIndividualmente()
    {
        // Arrange
        var loteId = 10;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "guia_001.pdf", "pedido_001.pdf", "guia_002.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).ComCaminho(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarEnviadosExtendAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var docIdCounter = 0;
        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => new DocumentoDto { Id = ++docIdCounter, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.SalvarExtendRunIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusDocumento>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _extendClientMock.Setup(e => e.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("file-id-xxx");
        _extendClientMock.Setup(e => e.IniciarExtracaoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("run-id-xxx");

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — 3 arquivos → 3 chamadas a UploadFileAsync e IniciarExtracaoAsync
        _extendClientMock.Verify(
            e => e.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "Deve enviar cada arquivo individualmente para a Extend via UploadFileAsync");

        _extendClientMock.Verify(
            e => e.IniciarExtracaoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "Deve iniciar uma extração por arquivo via IniciarExtracaoAsync");
    }

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_LoteValido_ClassificaGuiaSPSADTCorretamente()
    {
        // Arrange
        var loteId = 20;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "guia_sp_001.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).ComCaminho(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarEnviadosExtendAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DocumentoDto { Id = 1, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.SalvarExtendRunIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusDocumento>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _extendClientMock.Setup(e => e.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("file-id-guia");
        _extendClientMock.Setup(e => e.IniciarExtracaoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("run-id-guia");

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — arquivo com "guia_sp" → TipoDocumento.GuiaSPSADT
        _documentoServiceMock.Verify(
            s => s.AtualizarTipoAsync(1, TipoDocumento.GuiaSPSADT),
            Times.Once,
            "Arquivo com 'guia_sp' no nome deve ser classificado como GuiaSPSADT");

        // E deve usar o extractor correto para GuiaSPSADT
        _extendClientMock.Verify(
            e => e.IniciarExtracaoAsync("file-id-guia", ExtendOpts.ExtractorIdGuiaSPSADT, It.IsAny<CancellationToken>()),
            Times.Once,
            "Deve usar o ExtractorIdGuiaSPSADT para documentos do tipo GuiaSPSADT");
    }

    [Theory]
    [InlineData("pedido_medico_001.pdf", TipoDocumento.PedidoMedico)]
    [InlineData("solicitacao_exame.pdf", TipoDocumento.PedidoMedico)]
    [InlineData("laudo_patologia.pdf",   TipoDocumento.Laudo)]
    [InlineData("receita_medica.pdf",    TipoDocumento.Receita)]
    [InlineData("documento_desconhecido.pdf", TipoDocumento.Desconhecido)]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_ClassificacaoTipoDocumento_CorretoParaCadaNome(
        string nomeArquivo, TipoDocumento tipoEsperado)
    {
        // Arrange
        var loteId = 30;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, nomeArquivo);
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).ComCaminho(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarEnviadosExtendAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DocumentoDto { Id = 99, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.SalvarExtendRunIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusDocumento>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _extendClientMock.Setup(e => e.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("file-id-xxx");
        _extendClientMock.Setup(e => e.IniciarExtracaoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("run-id-xxx");

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — tipo classificado corretamente pelo nome
        _documentoServiceMock.Verify(
            s => s.AtualizarTipoAsync(99, tipoEsperado),
            Times.Once,
            $"Arquivo '{nomeArquivo}' deve ser classificado como {tipoEsperado}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TU-IMP-05: Lote não encontrado / ZIP corrompido
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_LoteNaoEncontrado_LancaInvalidOperationException()
    {
        // Arrange
        var loteId = 999;
        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync((LoteDto?)null);

        // Act
        var act = async () => await _sut.ProcessarLoteAsync(loteId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{loteId}*",
                "a mensagem de erro deve conter o ID do lote não encontrado");
    }

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_ZipNaoEncontrado_AtualizaStatusParaErro()
    {
        // Arrange
        var loteId = 55;
        var caminhoInexistente = Path.Combine(_tempDir, "nao_existe.zip");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(caminhoInexistente).ComCaminho(caminhoInexistente).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () => await _sut.ProcessarLoteAsync(loteId);

        // Assert
        await act.Should().ThrowAsync<Exception>(
            "uma exceção deve ser propagada quando o ZIP não existe");

        _loteServiceMock.Verify(
            s => s.AtualizarStatusAsync(loteId, StatusLote.Erro, It.IsAny<string>()),
            Times.Once,
            "O status do lote deve ser atualizado para Erro quando o processamento falha");
    }

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_ZipCorrompido_MensagemDeErroPersistida()
    {
        // Arrange
        var loteId = 56;
        var caminhoInexistente = Path.Combine(_tempDir, "corrompido.zip");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(caminhoInexistente).ComCaminho(caminhoInexistente).Build();

        string? mensagemCapturada = null;
        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(loteId, StatusLote.Erro, It.IsAny<string>()))
            .Callback<int, StatusLote, string?>((_, _, msg) => mensagemCapturada = msg)
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(loteId, StatusLote.Processando, null))
            .Returns(Task.CompletedTask);

        // Act
        try { await _sut.ProcessarLoteAsync(loteId); } catch { /* esperado */ }

        // Assert
        mensagemCapturada.Should().NotBeNullOrEmpty(
            "a mensagem de erro deve ser persistida para diagnóstico");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TU-IMP-06: Falha na Extend — documento marcado como Erro
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_ExtendFalha_DocumentoMarcadoComoErro()
    {
        // Arrange
        var loteId = 60;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "guia_001.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).ComCaminho(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarEnviadosExtendAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DocumentoDto { Id = 5, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusDocumento>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        // Simula falha na Extend
        _extendClientMock.Setup(e => e.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExtendApiException("Unauthorized: API key inválida"));

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — documento deve ser marcado como Erro
        _documentoServiceMock.Verify(
            s => s.AtualizarStatusAsync(5, StatusDocumento.Erro, It.Is<string>(m => m.Contains("Unauthorized"))),
            Times.Once,
            "Documento deve ser marcado como Erro quando a Extend retorna falha");
    }

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_TodosArquivosComErroExtend_StatusLoteErro()
    {
        // Arrange
        var loteId = 61;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "guia_001.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).ComCaminho(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DocumentoDto { Id = 1, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusDocumento>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _extendClientMock.Setup(e => e.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExtendApiException("Falha no upload"));

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — lote deve ser marcado como Erro quando todos os arquivos falharam
        _loteServiceMock.Verify(
            s => s.AtualizarStatusAsync(loteId, StatusLote.Erro, It.IsAny<string>()),
            Times.Once,
            "O lote deve ser marcado como Erro quando todos os arquivos falharam no envio para Extend");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TU-IMP-07: Salvar RunId no banco após envio para Extend
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_EnvioExtendSucesso_SalvaRunIdNoBanco()
    {
        // Arrange
        var loteId = 70;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "guia_sp_001.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).ComCaminho(zipPath).Build();
        var fileIdRetornado = "file-abc-123";
        var runIdRetornado  = "run-xyz-456";

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarEnviadosExtendAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DocumentoDto { Id = 7, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.SalvarExtendRunIdAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusDocumento>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _extendClientMock.Setup(e => e.UploadFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileIdRetornado);
        _extendClientMock.Setup(e => e.IniciarExtracaoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runIdRetornado);

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — RunId e FileId devem ser salvos no banco
        _documentoServiceMock.Verify(
            s => s.SalvarExtendRunIdAsync(7, fileIdRetornado, runIdRetornado, ExtendOpts.ExtractorIdGuiaSPSADT),
            Times.Once,
            "O RunId e FileId retornados pela Extend devem ser salvos no banco de dados");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Limpeza
    // ═══════════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);

        var uploadsDir = Path.Combine("wwwroot", "uploads", "lotes");
        if (Directory.Exists(uploadsDir))
            try { Directory.Delete(uploadsDir, recursive: true); } catch { /* ignora */ }
    }
}
