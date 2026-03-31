-- ============================================================
-- Sistema de Auditoria Extend
-- Script principal ajustado conforme entities + mapeamento EF
-- Criação do zero, sem migrations
-- ============================================================

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[RevisoesHumanas]') AND type = N'U')
    DROP TABLE [dbo].[RevisoesHumanas];

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DivergenciasAuditoria]') AND type = N'U')
    DROP TABLE [dbo].[DivergenciasAuditoria];

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Documentos]') AND type = N'U')
    DROP TABLE [dbo].[Documentos];

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AtendimentosAgrupados]') AND type = N'U')
    DROP TABLE [dbo].[AtendimentosAgrupados];

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Lotes]') AND type = N'U')
    DROP TABLE [dbo].[Lotes];
GO

-- ============================================================
-- Tabela: Lotes
-- ============================================================
CREATE TABLE [dbo].[Lotes]
(
    [Id]                         INT IDENTITY(1,1) NOT NULL,
    [NomeArquivo]                NVARCHAR(500) NOT NULL,
    [CaminhoArquivo]             NVARCHAR(1000) NOT NULL,
    [TamanhoArquivo]             BIGINT NOT NULL,
    [Status]                     INT NOT NULL DEFAULT 0,

    [QuantidadeDocumentos]       INT NOT NULL DEFAULT 0,
    [QuantidadeEnviadosExtend]   INT NOT NULL DEFAULT 0,
    [QuantidadeProcessados]      INT NOT NULL DEFAULT 0,
    [QuantidadeDivergencias]     INT NOT NULL DEFAULT 0,
    [QuantidadeRevisaoHumana]    INT NOT NULL DEFAULT 0,

    [MensagemErro]               NVARCHAR(2000) NULL,
    [DataInicioProcessamento]    DATETIME2 NULL,
    [DataFimProcessamento]       DATETIME2 NULL,

    -- Ajustado para refletir o comportamento atual do C#
    [DataCriacao]                DATETIME2 NULL CONSTRAINT [DF_Lotes_DataCriacao] DEFAULT GETUTCDATE(),
    [DataAtualizacao]            DATETIME2 NULL CONSTRAINT [DF_Lotes_DataAtualizacao] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_Lotes] PRIMARY KEY ([Id])
);
GO

-- ============================================================
-- Tabela: AtendimentosAgrupados
-- ============================================================
CREATE TABLE [dbo].[AtendimentosAgrupados]
(
    [Id]                          INT IDENTITY(1,1) NOT NULL,
    [LoteId]                      INT NOT NULL,

    -- Chave de agrupamento: numero_carteira do paciente (ou nome como fallback)
    [NumeroCarteira]              NVARCHAR(100) NULL,

    [NomePaciente]                NVARCHAR(200) NULL,
    [NomeMedico]                  NVARCHAR(200) NULL,
    [CrmMedico]                   NVARCHAR(50) NULL,
    [NumeroGuia]                  NVARCHAR(100) NULL,
    [NumeroPedido]                NVARCHAR(100) NULL,
    [DataAtendimento]             DATETIME2 NULL,

    [QuantidadeDocumentos]        INT NOT NULL DEFAULT 0,
    [QuantidadeDivergencias]      INT NOT NULL DEFAULT 0,
    [QuantidadeRevisaoHumana]     INT NOT NULL DEFAULT 0,

    [ScoreRisco]                  FLOAT NOT NULL DEFAULT 0,
    [RevisaoHumanaNecessaria]     BIT NOT NULL DEFAULT 0,

    [DataCriacao]                 DATETIME2 NULL CONSTRAINT [DF_AtendimentosAgrupados_DataCriacao] DEFAULT GETUTCDATE(),
    [DataAtualizacao]             DATETIME2 NULL CONSTRAINT [DF_AtendimentosAgrupados_DataAtualizacao] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_AtendimentosAgrupados] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AtendimentosAgrupados_Lotes]
        FOREIGN KEY ([LoteId]) REFERENCES [dbo].[Lotes]([Id]) ON DELETE CASCADE
);
GO

-- ============================================================
-- Tabela: Documentos
-- ============================================================
CREATE TABLE [dbo].[Documentos]
(
    [Id]                           INT IDENTITY(1,1) NOT NULL,
    [LoteId]                       INT NOT NULL,
    [NomeArquivo]                  NVARCHAR(500) NOT NULL,
    [CaminhoArquivo]               NVARCHAR(1000) NOT NULL,
    [TipoDocumento]                INT NOT NULL DEFAULT 0,
    [Status]                       INT NOT NULL DEFAULT 0,

    -- Integração Extend
    [ExtendFileId]                 NVARCHAR(200) NULL,
    [ExtendRunId]                  NVARCHAR(200) NULL,
    [ExtractorId]                  NVARCHAR(200) NULL,

    -- Dados extraídos
    [DadosExtraidos]               NVARCHAR(MAX) NULL,
    [ConfiancaOcr]                 FLOAT NOT NULL DEFAULT 0,
    [ReviewAgentScore]             INT NULL,

    -- Flags de auditoria
    [OrigemSuspeita]               BIT NOT NULL DEFAULT 0,
    [RevisaoHumanaNecessaria]      BIT NOT NULL DEFAULT 0,

    [MensagemErro]                 NVARCHAR(2000) NULL,
    [AtendimentoAgrupadoId]        INT NULL,

    [DataCriacao]                  DATETIME2 NULL CONSTRAINT [DF_Documentos_DataCriacao] DEFAULT GETUTCDATE(),
    [DataAtualizacao]              DATETIME2 NULL CONSTRAINT [DF_Documentos_DataAtualizacao] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_Documentos] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Documentos_Lotes]
        FOREIGN KEY ([LoteId]) REFERENCES [dbo].[Lotes]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Documentos_AtendimentosAgrupados]
        FOREIGN KEY ([AtendimentoAgrupadoId]) REFERENCES [dbo].[AtendimentosAgrupados]([Id]) ON DELETE NO ACTION
);
GO

-- ============================================================
-- Tabela: DivergenciasAuditoria
-- Ajustada conforme seu OnModelCreating
-- ============================================================
CREATE TABLE [dbo].[DivergenciasAuditoria]
(
    [Id]                         INT IDENTITY(1,1) NOT NULL,
    [DocumentoId]                INT NOT NULL,
    [AtendimentoAgrupadoId]      INT NULL,

    [Tipo]                       INT NOT NULL,
    [Severidade]                 INT NOT NULL,
    [Status]                     INT NOT NULL DEFAULT 0,

    [Descricao]                  NVARCHAR(500) NOT NULL,
    [DetalhesTecnicos]           NVARCHAR(2000) NULL,
    [ValorConfianca]             FLOAT NULL,
    [CampoAfetado]               NVARCHAR(100) NULL,
    [ValorEncontrado]            NVARCHAR(500) NULL,
    [ValorEsperado]              NVARCHAR(500) NULL,

    [DataCriacao]                DATETIME2 NULL CONSTRAINT [DF_DivergenciasAuditoria_DataCriacao] DEFAULT GETUTCDATE(),
    [DataAtualizacao]            DATETIME2 NULL CONSTRAINT [DF_DivergenciasAuditoria_DataAtualizacao] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_DivergenciasAuditoria] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_DivergenciasAuditoria_Documentos]
        FOREIGN KEY ([DocumentoId]) REFERENCES [dbo].[Documentos]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_DivergenciasAuditoria_AtendimentosAgrupados]
        FOREIGN KEY ([AtendimentoAgrupadoId]) REFERENCES [dbo].[AtendimentosAgrupados]([Id]) ON DELETE NO ACTION
);
GO

-- ============================================================
-- Tabela: RevisoesHumanas
-- ============================================================
CREATE TABLE [dbo].[RevisoesHumanas]
(
    [Id]                        INT IDENTITY(1,1) NOT NULL,
    [DivergenciaId]             INT NOT NULL,
    [Decisao]                   INT NOT NULL,
    [NomeAuditor]               NVARCHAR(200) NOT NULL,
    [Justificativa]             NVARCHAR(2000) NULL,
    [ObservacaoCorrecao]        NVARCHAR(2000) NULL,
    [DataRevisao]               DATETIME2 NOT NULL CONSTRAINT [DF_RevisoesHumanas_DataRevisao] DEFAULT GETUTCDATE(),

    [DataCriacao]               DATETIME2 NULL CONSTRAINT [DF_RevisoesHumanas_DataCriacao] DEFAULT GETUTCDATE(),
    [DataAtualizacao]           DATETIME2 NULL CONSTRAINT [DF_RevisoesHumanas_DataAtualizacao] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_RevisoesHumanas] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_RevisoesHumanas_DivergenciasAuditoria]
        FOREIGN KEY ([DivergenciaId]) REFERENCES [dbo].[DivergenciasAuditoria]([Id]) ON DELETE CASCADE,
    CONSTRAINT [UQ_RevisoesHumanas_DivergenciaId] UNIQUE ([DivergenciaId])
);
GO

-- ============================================================
-- Índices
-- ============================================================

CREATE INDEX [IX_Lotes_Status] ON [dbo].[Lotes]([Status]);
CREATE INDEX [IX_Lotes_DataCriacao] ON [dbo].[Lotes]([DataCriacao] DESC);

CREATE INDEX [IX_AtendimentosAgrupados_LoteId] ON [dbo].[AtendimentosAgrupados]([LoteId]);
CREATE INDEX [IX_AtendimentosAgrupados_NumeroCarteira] ON [dbo].[AtendimentosAgrupados]([LoteId], [NumeroCarteira]);
CREATE INDEX [IX_AtendimentosAgrupados_NumeroGuia] ON [dbo].[AtendimentosAgrupados]([NumeroGuia]);
CREATE INDEX [IX_AtendimentosAgrupados_NumeroPedido] ON [dbo].[AtendimentosAgrupados]([NumeroPedido]);
CREATE INDEX [IX_AtendimentosAgrupados_DataAtendimento] ON [dbo].[AtendimentosAgrupados]([DataAtendimento]);

CREATE INDEX [IX_Documentos_LoteId] ON [dbo].[Documentos]([LoteId]);
CREATE INDEX [IX_Documentos_Status] ON [dbo].[Documentos]([Status]);
CREATE INDEX [IX_Documentos_TipoDocumento] ON [dbo].[Documentos]([TipoDocumento]);
CREATE INDEX [IX_Documentos_ExtendRunId] ON [dbo].[Documentos]([ExtendRunId]);
CREATE INDEX [IX_Documentos_ExtendFileId] ON [dbo].[Documentos]([ExtendFileId]);
CREATE INDEX [IX_Documentos_AtendimentoAgrupadoId] ON [dbo].[Documentos]([AtendimentoAgrupadoId]);

CREATE INDEX [IX_DivergenciasAuditoria_DocumentoId] ON [dbo].[DivergenciasAuditoria]([DocumentoId]);
CREATE INDEX [IX_DivergenciasAuditoria_AtendimentoAgrupadoId] ON [dbo].[DivergenciasAuditoria]([AtendimentoAgrupadoId]);
CREATE INDEX [IX_DivergenciasAuditoria_Status] ON [dbo].[DivergenciasAuditoria]([Status]);
CREATE INDEX [IX_DivergenciasAuditoria_Severidade] ON [dbo].[DivergenciasAuditoria]([Severidade]);
CREATE INDEX [IX_DivergenciasAuditoria_Tipo] ON [dbo].[DivergenciasAuditoria]([Tipo]);

CREATE INDEX [IX_RevisoesHumanas_DivergenciaId] ON [dbo].[RevisoesHumanas]([DivergenciaId]);
CREATE INDEX [IX_RevisoesHumanas_NomeAuditor] ON [dbo].[RevisoesHumanas]([NomeAuditor]);
CREATE INDEX [IX_RevisoesHumanas_DataRevisao] ON [dbo].[RevisoesHumanas]([DataRevisao] DESC);
GO