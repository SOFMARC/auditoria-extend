using System.IO.Compression;

namespace AuditoriaExtend.Tests.Helpers;

/// <summary>
/// Utilitário para criação de arquivos ZIP em memória para testes.
/// Evita dependência de arquivos físicos no disco durante os testes.
/// </summary>
public static class ZipHelper
{
    /// <summary>
    /// Cria um Stream contendo um ZIP válido com os arquivos especificados.
    /// </summary>
    public static Stream CriarZipValido(params string[] nomesArquivos)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var nome in nomesArquivos)
            {
                var entry = zip.CreateEntry(nome);
                using var writer = new StreamWriter(entry.Open());
                writer.Write($"Conteúdo simulado do arquivo: {nome}");
            }
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Cria um Stream com um ZIP contendo documentos médicos típicos.
    /// </summary>
    public static Stream CriarZipComDocumentosMedicos()
    {
        return CriarZipValido(
            "guia_sp_001.pdf",
            "guia_sadt_002.pdf",
            "pedido_medico_003.pdf",
            "laudo_004.pdf"
        );
    }

    /// <summary>
    /// Cria um Stream com bytes inválidos (não é um ZIP real).
    /// </summary>
    public static Stream CriarArquivoInvalido()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03 }; // não é um ZIP
        return new MemoryStream(bytes);
    }

    /// <summary>
    /// Cria um Stream vazio (0 bytes).
    /// </summary>
    public static Stream CriarStreamVazio() => new MemoryStream(Array.Empty<byte>());

    /// <summary>
    /// Cria um arquivo ZIP físico temporário e retorna o caminho.
    /// O arquivo deve ser deletado pelo chamador após o uso.
    /// </summary>
    public static string CriarZipFisico(string diretorio, params string[] nomesArquivos)
    {
        var caminho = Path.Combine(diretorio, $"teste_{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(caminho, ZipArchiveMode.Create);
        foreach (var nome in nomesArquivos)
        {
            var entry = zip.CreateEntry(nome);
            using var writer = new StreamWriter(entry.Open());
            writer.Write($"Conteúdo simulado: {nome}");
        }
        return caminho;
    }
}
