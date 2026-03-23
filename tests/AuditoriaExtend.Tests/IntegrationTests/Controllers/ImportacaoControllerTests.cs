using AuditoriaExtend.Application.Common;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Domain.Enums;
using AuditoriaExtend.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using System.Net;
using System.Net.Http.Headers;
using AuditoriaExtend.Web.Controllers;

namespace AuditoriaExtend.Tests.IntegrationTests.Controllers;

/// <summary>
/// Testes de integração para ImportacaoController.
/// Utiliza WebApplicationFactory para testar o pipeline HTTP completo,
/// incluindo roteamento, model binding, validação e redirecionamentos.
/// </summary>
public class ImportacaoControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ImportacaoControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false // Permite verificar redirecionamentos manualmente
        });

        // Reseta o histórico de invocações dos mocks antes de cada teste
        // para evitar que chamadas de testes anteriores contaminem as verificações
        _factory.ImportacaoServiceMock.Invocations.Clear();
        _factory.LoteServiceMock.Invocations.Clear();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TI-IMP-01: Upload via formulário com sucesso
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Upload_POST_ArquivoZipValido_RedirecionaParaDetalhes()
    {
        // Arrange
        var loteRetornado = new LoteBuilder().ComId(7).ComNome("lote_teste.zip").Build();
        _factory.ImportacaoServiceMock
            .Setup(s => s.ReceberArquivoAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(loteRetornado);

        using var content = CriarMultipartComZip("lote_teste.zip");

        // Act
        var response = await _client.PostAsync("/Importacao/Upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect,
            "após upload bem-sucedido, o controller deve redirecionar");
        response.Headers.Location?.ToString().Should().Contain("Detalhes",
            "o redirecionamento deve ser para a página de Detalhes do lote");
    }

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Upload_POST_ArquivoZipValido_ChamaReceberArquivoAsync()
    {
        // Arrange
        var loteRetornado = new LoteBuilder().ComId(8).Build();
        _factory.ImportacaoServiceMock
            .Setup(s => s.ReceberArquivoAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>()))
            .ReturnsAsync(loteRetornado);

        using var content = CriarMultipartComZip("arquivo_valido.zip");

        // Act
        await _client.PostAsync("/Importacao/Upload", content);

        // Assert
        _factory.ImportacaoServiceMock.Verify(
            s => s.ReceberArquivoAsync(
                It.IsAny<Stream>(),
                It.Is<string>(n => n.EndsWith(".zip")),
                It.IsAny<long>()),
            Times.Once,
            "ReceberArquivoAsync deve ser chamado exatamente uma vez com arquivo .zip");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TI-IMP-02: Upload sem selecionar arquivo
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Upload_POST_SemArquivo_RetornaViewComErroDeModelState()
    {
        // Arrange — envia formulário vazio
        using var content = new MultipartFormDataContent();

        // Act
        var response = await _client.PostAsync("/Importacao/Upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "quando não há arquivo, deve retornar a view com erros (não redirecionar)");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("ZIP",
            "a mensagem de erro de validação deve aparecer na view");

        // Verifica que nenhuma chamada ao serviço foi feita neste teste específico
        // (o histórico foi limpo no construtor)
        _factory.ImportacaoServiceMock.Verify(
            s => s.ReceberArquivoAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>()),
            Times.Never,
            "ReceberArquivoAsync NÃO deve ser chamado quando não há arquivo");
    }

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Upload_POST_ArquivoComExtensaoInvalida_RetornaViewComErro()
    {
        // Arrange — envia um .pdf em vez de .zip
        using var content = CriarMultipartComArquivo("documento.pdf", "application/pdf",
            new byte[] { 0x25, 0x50, 0x44, 0x46 }); // magic bytes de PDF

        // Act
        var response = await _client.PostAsync("/Importacao/Upload", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "extensão inválida deve retornar a view com erros");

        var html = await response.Content.ReadAsStringAsync();
        html.Should().ContainAny("ZIP", "zip", "extensão",
            "a mensagem de erro deve mencionar que apenas ZIP é aceito");

        // Verifica que nenhuma chamada ao serviço foi feita neste teste específico
        _factory.ImportacaoServiceMock.Verify(
            s => s.ReceberArquivoAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>()),
            Times.Never,
            "ReceberArquivoAsync NÃO deve ser chamado para extensão inválida");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TI-IMP-03: Listagem de histórico com paginação
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Historico_GET_SemFiltros_RetornaViewComStatusOk()
    {
        // Arrange
        var lotes = GerarListaLotes(15);
        var paginado = new PaginatedList<LoteDto>(lotes.Take(10).ToList(), 15, 1, 10);
        _factory.LoteServiceMock
            .Setup(s => s.ListarAsync(It.IsAny<PagedRequest>(), null))
            .ReturnsAsync(paginado);

        // Act
        var response = await _client.GetAsync("/Importacao/Historico");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.LoteServiceMock.Verify(
            s => s.ListarAsync(It.IsAny<PagedRequest>(), null),
            Times.Once);
    }

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Historico_GET_ComPaginacao_PassaParametrosCorretosParaServico()
    {
        // Arrange
        var paginado = new PaginatedList<LoteDto>(new List<LoteDto>(), 0, 2, 5);
        _factory.LoteServiceMock
            .Setup(s => s.ListarAsync(It.IsAny<PagedRequest>(), null))
            .ReturnsAsync(paginado);

        // Act
        await _client.GetAsync("/Importacao/Historico?page=2&pageSize=5&sortBy=NomeArquivo&sortOrder=asc");

        // Assert — verifica que os parâmetros de paginação foram passados corretamente
        _factory.LoteServiceMock.Verify(
            s => s.ListarAsync(
                It.Is<PagedRequest>(r =>
                    r.Page == 2 &&
                    r.PageSize == 5 &&
                    r.SortBy == "NomeArquivo" &&
                    r.SortOrder == "asc"),
                null),
            Times.Once,
            "Os parâmetros de paginação da query string devem ser repassados ao serviço");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TI-IMP-04: Filtro de histórico por status
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0, "Pendente")]
    [InlineData(1, "Processando")]
    [InlineData(2, "Concluido")]
    [InlineData(3, "Erro")]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Historico_GET_ComFiltroStatus_PassaFiltroCorretoParaServico(
        int statusInt, string descricaoStatus)
    {
        // Arrange
        var paginado = new PaginatedList<LoteDto>(new List<LoteDto>(), 0, 1, 10);
        _factory.LoteServiceMock
            .Setup(s => s.ListarAsync(It.IsAny<PagedRequest>(), statusInt))
            .ReturnsAsync(paginado);

        // Act
        var response = await _client.GetAsync($"/Importacao/Historico?filterStatus={statusInt}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"filtro por status '{descricaoStatus}' deve retornar OK");

        _factory.LoteServiceMock.Verify(
            s => s.ListarAsync(It.IsAny<PagedRequest>(), statusInt),
            Times.Once,
            $"O filtro de status {statusInt} ({descricaoStatus}) deve ser passado ao serviço");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Detalhes do lote
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Detalhes_GET_LoteExistente_RetornaViewComStatusOk()
    {
        // Arrange
        var lote = new LoteBuilder().ComId(1).ComStatus(StatusLote.Concluido).ComDocumentos(5, 5).Build();
        _factory.LoteServiceMock.Setup(s => s.ObterPorIdAsync(1)).ReturnsAsync(lote);

        // Act
        var response = await _client.GetAsync("/Importacao/Detalhes/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Detalhes_GET_LoteInexistente_RetornaNotFound()
    {
        // Arrange
        _factory.LoteServiceMock.Setup(s => s.ObterPorIdAsync(9999)).ReturnsAsync((LoteDto?)null);

        // Act
        var response = await _client.GetAsync("/Importacao/Detalhes/9999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "lote inexistente deve retornar 404 Not Found");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Processar lote
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Processar_POST_LoteValido_RedirecionaParaDetalhes()
    {
        // Arrange
        _factory.ImportacaoServiceMock
            .Setup(s => s.ProcessarLoteAsync(5))
            .Returns(Task.CompletedTask);

        using var content = new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>());

        // Act
        var response = await _client.PostAsync("/Importacao/Processar/5", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect,
            "após processar com sucesso, deve redirecionar para Detalhes");
        response.Headers.Location?.ToString().Should().Contain("Detalhes");
    }

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Processar_POST_ErroNoProcessamento_RedirecionaParaDetalhesComTempDataErro()
    {
        // Arrange
        _factory.ImportacaoServiceMock
            .Setup(s => s.ProcessarLoteAsync(6))
            .ThrowsAsync(new InvalidOperationException("ZIP corrompido"));

        using var content = new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>());

        // Act
        var response = await _client.PostAsync("/Importacao/Processar/6", content);

        // Assert — mesmo com erro, deve redirecionar (não retornar 500)
        response.StatusCode.Should().Be(HttpStatusCode.Redirect,
            "mesmo com erro no processamento, deve redirecionar para Detalhes (não 500)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Deletar lote
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Categoria", "Integracao")]
    [Trait("Modulo", "Importacao")]
    public async Task Deletar_POST_LoteExistente_RedirecionaParaHistorico()
    {
        // Arrange
        _factory.LoteServiceMock.Setup(s => s.DeletarAsync(3)).Returns(Task.CompletedTask);
        using var content = new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>());

        // Act
        var response = await _client.PostAsync("/Importacao/Deletar/3", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().Contain("Historico",
            "após deletar, deve redirecionar para o Histórico");

        _factory.LoteServiceMock.Verify(s => s.DeletarAsync(3), Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers privados
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Cria um MultipartFormDataContent com um arquivo ZIP em memória.</summary>
    private static MultipartFormDataContent CriarMultipartComZip(string nomeArquivo)
    {
        var zipStream = ZipHelper.CriarZipValido("guia_001.pdf", "pedido_001.pdf");
        var zipBytes = ((MemoryStream)zipStream).ToArray();
        return CriarMultipartComArquivo(nomeArquivo, "application/zip", zipBytes);
    }

    /// <summary>Cria um MultipartFormDataContent com bytes e content-type arbitrários.</summary>
    private static MultipartFormDataContent CriarMultipartComArquivo(
        string nomeArquivo, string contentType, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "arquivo", nomeArquivo);
        return content;
    }

    /// <summary>Gera uma lista de LoteDtos para testes de paginação.</summary>
    private static List<LoteDto> GerarListaLotes(int quantidade)
    {
        return Enumerable.Range(1, quantidade)
            .Select(i => new LoteBuilder()
                .ComId(i)
                .ComNome($"lote_{i:D3}.zip")
                .ComStatus(i % 2 == 0 ? StatusLote.Concluido : StatusLote.Pendente)
                .Build())
            .ToList();
    }
}
