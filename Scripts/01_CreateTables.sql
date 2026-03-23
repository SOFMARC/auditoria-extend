-- ============================================================
-- Sistema de Auditoria de Pedidos e Guias Médicas
-- Script: 01_CreateTables.sql  |  Versão: 2.0 (Clean Architecture)
-- Execute no SQL Server Management Studio ou Azure Data Studio
-- ============================================================

-- Crie o banco antes de executar (ajuste o nome se necessário):
-- CREATE DATABASE AuditoriaExtendDB;
-- GO
-- USE AuditoriaExtendDB;
-- GO

-- ============================================================
-- Tabela: Lotes
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Lotes')
BEGIN
    CREATE TABLE Lotes (
        Id                      INT IDENTITY(1,1) NOT NULL,
        NomeArquivo             NVARCHAR(500)     NOT NULL,
        CaminhoArquivo          NVARCHAR(1000)    NULL,
        TamanhoArquivo          BIGINT            NOT NULL DEFAULT 0,
        Status                  INT               NOT NULL DEFAULT 0,
            -- 0=Pendente | 1=Processando | 2=Concluido | 3=Erro
        TotalDocumentos         INT               NOT NULL DEFAULT 0,
        DocumentosProcessados   INT               NOT NULL DEFAULT 0,
        DocumentosComErro       INT               NOT NULL DEFAULT 0,
        MensagemErro            NVARCHAR(2000)    NULL,
        DataCriacao             DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        DataAtualizacao         DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        DataInicioProcessamento DATETIME2         NULL,
        DataFimProcessamento    DATETIME2         NULL,
        CONSTRAINT PK_Lotes PRIMARY KEY (Id)
    );
    PRINT 'Tabela Lotes criada.';
END
ELSE PRINT 'Tabela Lotes ja existe.';
GO

-- ============================================================
-- Tabela: Documentos
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Documentos')
BEGIN
    CREATE TABLE Documentos (
        Id              INT IDENTITY(1,1) NOT NULL,
        LoteId          INT               NOT NULL,
        NomeArquivo     NVARCHAR(500)     NOT NULL,
        CaminhoArquivo  NVARCHAR(1000)    NULL,
        TamanhoArquivo  BIGINT            NOT NULL DEFAULT 0,
        TipoDocumento   INT               NOT NULL DEFAULT 0,
            -- 0=Desconhecido | 1=GuiaSP | 2=GuiaSADT | 3=PedidoMedico | 4=Laudo | 5=Outro
        Status          INT               NOT NULL DEFAULT 0,
            -- 0=Pendente | 1=Processando | 2=Concluido | 3=Erro
        ConfiancaOCR    FLOAT             NULL,
        JsonExtracao    NVARCHAR(MAX)     NULL,
        MensagemErro    NVARCHAR(2000)    NULL,
        DataCriacao     DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        DataAtualizacao DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT PK_Documentos PRIMARY KEY (Id),
        CONSTRAINT FK_Documentos_Lotes FOREIGN KEY (LoteId)
            REFERENCES Lotes(Id) ON DELETE CASCADE
    );
    PRINT 'Tabela Documentos criada.';
END
ELSE PRINT 'Tabela Documentos ja existe.';
GO

-- ============================================================
-- Tabela: AtendimentosAgrupados
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AtendimentosAgrupados')
BEGIN
    CREATE TABLE AtendimentosAgrupados (
        Id                  INT IDENTITY(1,1) NOT NULL,
        LoteId              INT               NOT NULL,
        NomePaciente        NVARCHAR(200)     NULL,
        NomeMedico          NVARCHAR(200)     NULL,
        NumeroGuia          NVARCHAR(50)      NULL,
        NumeroPedido        NVARCHAR(50)      NULL,
        DataAtendimento     DATE              NULL,
        TotalDocumentos     INT               NOT NULL DEFAULT 0,
        TotalProcedimentos  INT               NOT NULL DEFAULT 0,
        TotalDivergencias   INT               NOT NULL DEFAULT 0,
        DataCriacao         DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        DataAtualizacao     DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT PK_AtendimentosAgrupados PRIMARY KEY (Id),
        CONSTRAINT FK_Atendimentos_Lotes FOREIGN KEY (LoteId)
            REFERENCES Lotes(Id) ON DELETE CASCADE
    );
    PRINT 'Tabela AtendimentosAgrupados criada.';
END
ELSE PRINT 'Tabela AtendimentosAgrupados ja existe.';
GO

-- ============================================================
-- Tabela: DivergenciasAuditoria
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DivergenciasAuditoria')
BEGIN
    CREATE TABLE DivergenciasAuditoria (
        Id                    INT IDENTITY(1,1) NOT NULL,
        DocumentoId           INT               NOT NULL,
        AtendimentoAgrupadoId INT               NULL,
        Tipo                  INT               NOT NULL,
            -- 1=ConfiancaBaixa | 2=ProcedimentoNaoEncontrado | 3=Duplicidade
            -- 4=OrigemSuspeita | 5=ValorForaDoPadrao | 6=DataInvalida | 7=CampoObrigatorioAusente
        Severidade            INT               NOT NULL,
            -- 1=Baixa | 2=Media | 3=Alta | 4=Critica
        Status                INT               NOT NULL DEFAULT 0,
            -- 0=Pendente | 1=EmRevisao | 2=Aceita | 3=Rejeitada | 4=CorrecaoSolicitada
        Descricao             NVARCHAR(500)     NOT NULL,
        DetalhesTecnicos      NVARCHAR(2000)    NULL,
        CampoAfetado          NVARCHAR(100)     NULL,
        ValorEncontrado       NVARCHAR(500)     NULL,
        ValorEsperado         NVARCHAR(500)     NULL,
        ValorConfianca        FLOAT             NULL,
        DataCriacao           DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        DataAtualizacao       DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT PK_DivergenciasAuditoria PRIMARY KEY (Id),
        CONSTRAINT FK_Divergencias_Documentos FOREIGN KEY (DocumentoId)
            REFERENCES Documentos(Id) ON DELETE CASCADE,
        CONSTRAINT FK_Divergencias_Atendimentos FOREIGN KEY (AtendimentoAgrupadoId)
            REFERENCES AtendimentosAgrupados(Id) ON DELETE NO ACTION
    );
    PRINT 'Tabela DivergenciasAuditoria criada.';
END
ELSE PRINT 'Tabela DivergenciasAuditoria ja existe.';
GO

-- ============================================================
-- Tabela: RevisoesHumanas
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'RevisoesHumanas')
BEGIN
    CREATE TABLE RevisoesHumanas (
        Id                  INT IDENTITY(1,1) NOT NULL,
        DivergenciaId       INT               NOT NULL,
        Decisao             INT               NOT NULL,
            -- 2=Aceita | 3=Rejeitada | 4=CorrecaoSolicitada
        NomeAuditor         NVARCHAR(200)     NOT NULL,
        Justificativa       NVARCHAR(1000)    NULL,
        ObservacaoCorrecao  NVARCHAR(1000)    NULL,
        DataRevisao         DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        DataCriacao         DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        DataAtualizacao     DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT PK_RevisoesHumanas PRIMARY KEY (Id),
        CONSTRAINT FK_Revisoes_Divergencias FOREIGN KEY (DivergenciaId)
            REFERENCES DivergenciasAuditoria(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_Revisoes_Divergencia UNIQUE (DivergenciaId)
    );
    PRINT 'Tabela RevisoesHumanas criada.';
END
ELSE PRINT 'Tabela RevisoesHumanas ja existe.';
GO

PRINT '==========================================================';
PRINT '01_CreateTables.sql concluido com sucesso!';
PRINT '==========================================================';
GO
