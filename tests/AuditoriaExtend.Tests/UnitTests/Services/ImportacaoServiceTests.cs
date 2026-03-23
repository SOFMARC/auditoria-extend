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
/// Valida as regras de negócio da camada Application de forma isolada,
/// usando mocks para todas as dependências externas.
/// </summary>
public class ImportacaoServiceTests : IDisposable
{
    // ── Mocks das dependências ──────────────────────────────────────────────
    private readonly Mock<ILoteService> _loteServiceMock;
    private readonly Mock<IDocumentoService> _documentoServiceMock;
    private readonly Mock<IAuditoriaRegraService> _auditoriaServiceMock;

    // ── Sistema sob teste ───────────────────────────────────────────────────
    private readonly ImportacaoService _sut;

    // ── Diretório temporário para arquivos físicos ──────────────────────────
    private readonly string _tempDir;

    public ImportacaoServiceTests()
    {
        _loteServiceMock = new Mock<ILoteService>();
        _documentoServiceMock = new Mock<IDocumentoService>();
        _auditoriaServiceMock = new Mock<IAuditoriaRegraService>();

        _sut = new ImportacaoService(
            _loteServiceMock.Object,
            _documentoServiceMock.Object,
            _auditoriaServiceMock.Object
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
        var tamanho = 2 * 1024 * 1024L; // 2 MB
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
    // TU-IMP-04: Processamento de lote válido
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_LoteValido_AtualizaStatusParaProcessandoEConcluido()
    {
        // Arrange
        var loteId = 42;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "guia_sp_001.pdf", "pedido_001.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarProcessadosAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DocumentoDto { Id = 1, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _auditoriaServiceMock.Setup(s => s.AuditarDocumentoAsync(It.IsAny<int>())).ReturnsAsync(0);
        _auditoriaServiceMock.Setup(s => s.DetectarDuplicidadesAsync(It.IsAny<int>())).ReturnsAsync(0);

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — verifica a sequência: Processando → Concluido
        _loteServiceMock.Verify(
            s => s.AtualizarStatusAsync(loteId, StatusLote.Processando, null),
            Times.Once,
            "O status deve ser atualizado para Processando no início");

        _loteServiceMock.Verify(
            s => s.AtualizarStatusAsync(loteId, StatusLote.Concluido, null),
            Times.Once,
            "O status deve ser atualizado para Concluido ao final");
    }

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_LoteValido_CriaDocumentosParaCadaArquivoPdf()
    {
        // Arrange
        var loteId = 10;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "guia_001.pdf", "pedido_001.pdf", "laudo_001.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarProcessadosAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var docIdCounter = 0;
        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => new DocumentoDto { Id = ++docIdCounter, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _auditoriaServiceMock.Setup(s => s.AuditarDocumentoAsync(It.IsAny<int>())).ReturnsAsync(0);
        _auditoriaServiceMock.Setup(s => s.DetectarDuplicidadesAsync(It.IsAny<int>())).ReturnsAsync(0);

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — 3 arquivos PDF → 3 chamadas a CriarAsync
        _documentoServiceMock.Verify(
            s => s.CriarAsync(loteId, It.IsAny<string>(), It.IsAny<string>()),
            Times.Exactly(3),
            "Deve criar um documento para cada arquivo PDF no ZIP");
    }

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_LoteValido_ClassificaCorretamenteTipoGuia()
    {
        // Arrange
        var loteId = 20;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "guia_sp_001.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarProcessadosAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DocumentoDto { Id = 1, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _auditoriaServiceMock.Setup(s => s.AuditarDocumentoAsync(It.IsAny<int>())).ReturnsAsync(0);
        _auditoriaServiceMock.Setup(s => s.DetectarDuplicidadesAsync(It.IsAny<int>())).ReturnsAsync(0);

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — arquivo com "guia_sp" no nome → TipoDocumento.GuiaSPSADT
        _documentoServiceMock.Verify(
            s => s.AtualizarTipoAsync(1, TipoDocumento.GuiaSPSADT),
            Times.Once,
            "Arquivo com 'guia_sp' no nome deve ser classificado como GuiaSPSADT");
    }

    [Theory]
    [InlineData("pedido_medico_001.pdf", TipoDocumento.PedidoMedico)]
    [InlineData("solicitacao_exame.pdf", TipoDocumento.PedidoMedico)]
    [InlineData("laudo_patologia.pdf", TipoDocumento.Laudo)]
    [InlineData("receita_medica.pdf", TipoDocumento.Receita)]
    [InlineData("documento_desconhecido.pdf", TipoDocumento.Desconhecido)]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_ClassificacaoTipoDocumento_CorretoParaCadaNome(
        string nomeArquivo, TipoDocumento tipoEsperado)
    {
        // Arrange
        var loteId = 30;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, nomeArquivo);
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarProcessadosAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new DocumentoDto { Id = 99, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _auditoriaServiceMock.Setup(s => s.AuditarDocumentoAsync(It.IsAny<int>())).ReturnsAsync(0);
        _auditoriaServiceMock.Setup(s => s.DetectarDuplicidadesAsync(It.IsAny<int>())).ReturnsAsync(0);

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert
        _documentoServiceMock.Verify(
            s => s.AtualizarTipoAsync(99, tipoEsperado),
            Times.Once,
            $"Arquivo '{nomeArquivo}' deve ser classificado como {tipoEsperado}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TU-IMP-05: Processamento de ZIP corrompido
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
    public async Task ProcessarLoteAsync_ZipCorrompido_AtualizaStatusParaErro()
    {
        // Arrange
        var loteId = 55;
        // Caminho de arquivo que não existe → ZipFile.ExtractToDirectory lança exceção
        var caminhoInexistente = Path.Combine(_tempDir, "nao_existe.zip");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(caminhoInexistente).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        // Act
        var act = async () => await _sut.ProcessarLoteAsync(loteId);

        // Assert — deve propagar a exceção e ter atualizado o status para Erro
        await act.Should().ThrowAsync<Exception>(
            "uma exceção deve ser propagada quando o ZIP não pode ser processado");

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
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(caminhoInexistente).Build();

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
    // Testes de auditoria e detecção de duplicidades
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_LoteValido_ExecutaAuditoriaPorDocumento()
    {
        // Arrange
        var loteId = 70;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "guia_001.pdf", "pedido_001.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarProcessadosAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var docIdCounter = 0;
        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => new DocumentoDto { Id = ++docIdCounter, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _auditoriaServiceMock.Setup(s => s.AuditarDocumentoAsync(It.IsAny<int>())).ReturnsAsync(0);
        _auditoriaServiceMock.Setup(s => s.DetectarDuplicidadesAsync(It.IsAny<int>())).ReturnsAsync(0);

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert — 2 documentos → 2 chamadas a AuditarDocumentoAsync
        _auditoriaServiceMock.Verify(
            s => s.AuditarDocumentoAsync(It.IsAny<int>()),
            Times.Exactly(2),
            "AuditarDocumentoAsync deve ser chamado uma vez por documento");

        // E DetectarDuplicidades deve ser chamado uma vez ao final do lote
        _auditoriaServiceMock.Verify(
            s => s.DetectarDuplicidadesAsync(loteId),
            Times.Once,
            "DetectarDuplicidadesAsync deve ser chamado uma vez ao final do processamento do lote");
    }

    [Fact]
    [Trait("Categoria", "Unitario")]
    [Trait("Modulo", "Importacao")]
    public async Task ProcessarLoteAsync_LoteValido_IncrementaContadorPorDocumento()
    {
        // Arrange
        var loteId = 80;
        var zipPath = ZipHelper.CriarZipFisico(_tempDir, "doc1.pdf", "doc2.pdf", "doc3.pdf");
        var loteDto = new LoteBuilder().ComId(loteId).ComNome(zipPath).Build();

        _loteServiceMock.Setup(s => s.ObterPorIdAsync(loteId)).ReturnsAsync(loteDto);
        _loteServiceMock.Setup(s => s.AtualizarStatusAsync(It.IsAny<int>(), It.IsAny<StatusLote>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _loteServiceMock.Setup(s => s.IncrementarProcessadosAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var counter = 0;
        _documentoServiceMock.Setup(s => s.CriarAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(() => new DocumentoDto { Id = ++counter, LoteId = loteId });
        _documentoServiceMock.Setup(s => s.AtualizarTipoAsync(It.IsAny<int>(), It.IsAny<TipoDocumento>()))
            .Returns(Task.CompletedTask);
        _auditoriaServiceMock.Setup(s => s.AuditarDocumentoAsync(It.IsAny<int>())).ReturnsAsync(0);
        _auditoriaServiceMock.Setup(s => s.DetectarDuplicidadesAsync(It.IsAny<int>())).ReturnsAsync(0);

        // Act
        await _sut.ProcessarLoteAsync(loteId);

        // Assert
        _loteServiceMock.Verify(
            s => s.IncrementarProcessadosAsync(loteId),
            Times.Exactly(3),
            "IncrementarProcessadosAsync deve ser chamado uma vez por documento processado");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Limpeza
    // ═══════════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        // Limpa arquivos temporários criados durante os testes
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);

        // Limpa a pasta de uploads criada pelo ImportacaoService
        var uploadsDir = Path.Combine("wwwroot", "uploads", "lotes");
        if (Directory.Exists(uploadsDir))
            try { Directory.Delete(uploadsDir, recursive: true); } catch { /* ignora */ }
    }
}
