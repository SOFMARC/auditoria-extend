namespace AuditoriaExtend.Domain.Enums;

public enum StatusLote
{
    Pendente = 0,
    Processando = 1,
    Concluido = 2,
    Erro = 3
}

public enum TipoDocumento
{
    Desconhecido = 0,
    GuiaSPSADT = 1,
    PedidoMedico = 2,
    Laudo = 3,
    Receita = 4
}

public enum StatusDocumento
{
    Pendente = 0,
    EmProcessamento = 1,
    Processado = 2,
    Erro = 3
}

public enum SeveridadeDivergencia
{
    Baixa = 1,
    Media = 2,
    Alta = 3,
    Critica = 4
}

public enum StatusDivergencia
{
    Pendente = 0,
    EmRevisao = 1,
    Aceita = 2,
    Rejeitada = 3,
    CorrecaoSolicitada = 4
}

public enum TipoDivergencia
{
    ConfiancaBaixa = 1,
    ProcedimentoNaoEncontrado = 2,
    DuplicidadeDetectada = 3,
    OrigemSuspeita = 4,
    ValorForaDoPadrao = 5,
    DataInvalida = 6,
    CampoObrigatorioAusente = 7
}
