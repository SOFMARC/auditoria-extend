-- ============================================================
-- Sistema de Auditoria Extend
-- Script: 04_AlterTables_AddMissingColumns.sql
-- Versao: 2.0 - Adiciona colunas faltantes em bancos EXISTENTES
--
-- Execute este script se o banco ja existe e foi criado com
-- uma versao anterior do 01_CreateTables.sql.
-- Cada bloco e idempotente (pode ser executado multiplas vezes).
-- ============================================================

PRINT '=== Iniciando migracao 04_AlterTables_AddMissingColumns ===';
GO

-- ============================================================
-- Tabela: Lotes
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'QuantidadeDocumentos')
BEGIN
    -- Migrar dados de TotalDocumentos se existir
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'TotalDocumentos')
    BEGIN
        EXEC sp_rename 'Lotes.TotalDocumentos', 'QuantidadeDocumentos', 'COLUMN';
        PRINT 'Lotes.TotalDocumentos renomeado para QuantidadeDocumentos.';
    END
    ELSE
    BEGIN
        ALTER TABLE Lotes ADD QuantidadeDocumentos INT NOT NULL DEFAULT 0;
        PRINT 'Coluna QuantidadeDocumentos adicionada a Lotes.';
    END
END
ELSE PRINT 'Lotes.QuantidadeDocumentos ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'QuantidadeEnviadosExtend')
BEGIN
    ALTER TABLE Lotes ADD QuantidadeEnviadosExtend INT NOT NULL DEFAULT 0;
    PRINT 'Coluna QuantidadeEnviadosExtend adicionada a Lotes.';
END
ELSE PRINT 'Lotes.QuantidadeEnviadosExtend ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'QuantidadeProcessados')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'DocumentosProcessados')
    BEGIN
        EXEC sp_rename 'Lotes.DocumentosProcessados', 'QuantidadeProcessados', 'COLUMN';
        PRINT 'Lotes.DocumentosProcessados renomeado para QuantidadeProcessados.';
    END
    ELSE
    BEGIN
        ALTER TABLE Lotes ADD QuantidadeProcessados INT NOT NULL DEFAULT 0;
        PRINT 'Coluna QuantidadeProcessados adicionada a Lotes.';
    END
END
ELSE PRINT 'Lotes.QuantidadeProcessados ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'QuantidadeDivergencias')
BEGIN
    ALTER TABLE Lotes ADD QuantidadeDivergencias INT NOT NULL DEFAULT 0;
    PRINT 'Coluna QuantidadeDivergencias adicionada a Lotes.';
END
ELSE PRINT 'Lotes.QuantidadeDivergencias ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'QuantidadeRevisaoHumana')
BEGIN
    ALTER TABLE Lotes ADD QuantidadeRevisaoHumana INT NOT NULL DEFAULT 0;
    PRINT 'Coluna QuantidadeRevisaoHumana adicionada a Lotes.';
END
ELSE PRINT 'Lotes.QuantidadeRevisaoHumana ja existe.';
GO

-- Renomear DataInicioProcessamento se houver typo antigo
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'DataInicioProcesamento')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'DataInicioProcessamento')
    BEGIN
        EXEC sp_rename 'Lotes.DataInicioProcesamento', 'DataInicioProcessamento', 'COLUMN';
        PRINT 'Lotes.DataInicioProcesamento renomeado para DataInicioProcessamento.';
    END
END
GO

-- ============================================================
-- Tabela: AtendimentosAgrupados
-- ============================================================

-- NumeroCarteira: COLUNA CRITICA - chave de agrupamento por paciente
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'NumeroCarteira')
BEGIN
    ALTER TABLE AtendimentosAgrupados ADD NumeroCarteira NVARCHAR(100) NULL;
    PRINT 'Coluna NumeroCarteira adicionada a AtendimentosAgrupados.';
END
ELSE PRINT 'AtendimentosAgrupados.NumeroCarteira ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'CrmMedico')
BEGIN
    ALTER TABLE AtendimentosAgrupados ADD CrmMedico NVARCHAR(50) NULL;
    PRINT 'Coluna CrmMedico adicionada a AtendimentosAgrupados.';
END
ELSE PRINT 'AtendimentosAgrupados.CrmMedico ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'QuantidadeDocumentos')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'TotalDocumentos')
    BEGIN
        EXEC sp_rename 'AtendimentosAgrupados.TotalDocumentos', 'QuantidadeDocumentos', 'COLUMN';
        PRINT 'AtendimentosAgrupados.TotalDocumentos renomeado para QuantidadeDocumentos.';
    END
    ELSE
    BEGIN
        ALTER TABLE AtendimentosAgrupados ADD QuantidadeDocumentos INT NOT NULL DEFAULT 0;
        PRINT 'Coluna QuantidadeDocumentos adicionada a AtendimentosAgrupados.';
    END
END
ELSE PRINT 'AtendimentosAgrupados.QuantidadeDocumentos ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'QuantidadeDivergencias')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'TotalDivergencias')
    BEGIN
        EXEC sp_rename 'AtendimentosAgrupados.TotalDivergencias', 'QuantidadeDivergencias', 'COLUMN';
        PRINT 'AtendimentosAgrupados.TotalDivergencias renomeado para QuantidadeDivergencias.';
    END
    ELSE
    BEGIN
        ALTER TABLE AtendimentosAgrupados ADD QuantidadeDivergencias INT NOT NULL DEFAULT 0;
        PRINT 'Coluna QuantidadeDivergencias adicionada a AtendimentosAgrupados.';
    END
END
ELSE PRINT 'AtendimentosAgrupados.QuantidadeDivergencias ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'QuantidadeRevisaoHumana')
BEGIN
    ALTER TABLE AtendimentosAgrupados ADD QuantidadeRevisaoHumana INT NOT NULL DEFAULT 0;
    PRINT 'Coluna QuantidadeRevisaoHumana adicionada a AtendimentosAgrupados.';
END
ELSE PRINT 'AtendimentosAgrupados.QuantidadeRevisaoHumana ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'ScoreRisco')
BEGIN
    ALTER TABLE AtendimentosAgrupados ADD ScoreRisco FLOAT NOT NULL DEFAULT 0.0;
    PRINT 'Coluna ScoreRisco adicionada a AtendimentosAgrupados.';
END
ELSE PRINT 'AtendimentosAgrupados.ScoreRisco ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'RevisaoHumanaNecessaria')
BEGIN
    ALTER TABLE AtendimentosAgrupados ADD RevisaoHumanaNecessaria BIT NOT NULL DEFAULT 0;
    PRINT 'Coluna RevisaoHumanaNecessaria adicionada a AtendimentosAgrupados.';
END
ELSE PRINT 'AtendimentosAgrupados.RevisaoHumanaNecessaria ja existe.';
GO

-- ============================================================
-- Tabela: Documentos
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ExtendFileId')
BEGIN
    ALTER TABLE Documentos ADD ExtendFileId NVARCHAR(200) NULL;
    PRINT 'Coluna ExtendFileId adicionada a Documentos.';
END
ELSE PRINT 'Documentos.ExtendFileId ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ExtendRunId')
BEGIN
    ALTER TABLE Documentos ADD ExtendRunId NVARCHAR(200) NULL;
    PRINT 'Coluna ExtendRunId adicionada a Documentos.';
END
ELSE PRINT 'Documentos.ExtendRunId ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ExtractorId')
BEGIN
    ALTER TABLE Documentos ADD ExtractorId NVARCHAR(200) NULL;
    PRINT 'Coluna ExtractorId adicionada a Documentos.';
END
ELSE PRINT 'Documentos.ExtractorId ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'DadosExtraidos')
BEGIN
    ALTER TABLE Documentos ADD DadosExtraidos NVARCHAR(MAX) NULL;
    PRINT 'Coluna DadosExtraidos adicionada a Documentos.';
END
ELSE PRINT 'Documentos.DadosExtraidos ja existe.';
GO

-- ConfiancaOcr: renomear ConfiancaOCR (nome antigo) se necessario
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ConfiancaOcr')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ConfiancaOCR')
    BEGIN
        EXEC sp_rename 'Documentos.ConfiancaOCR', 'ConfiancaOcr', 'COLUMN';
        PRINT 'Documentos.ConfiancaOCR renomeado para ConfiancaOcr.';
    END
    ELSE
    BEGIN
        ALTER TABLE Documentos ADD ConfiancaOcr FLOAT NOT NULL DEFAULT 0.0;
        PRINT 'Coluna ConfiancaOcr adicionada a Documentos.';
    END
END
ELSE PRINT 'Documentos.ConfiancaOcr ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ReviewAgentScore')
BEGIN
    ALTER TABLE Documentos ADD ReviewAgentScore INT NULL;
    PRINT 'Coluna ReviewAgentScore adicionada a Documentos.';
END
ELSE PRINT 'Documentos.ReviewAgentScore ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'OrigemSuspeita')
BEGIN
    ALTER TABLE Documentos ADD OrigemSuspeita BIT NOT NULL DEFAULT 0;
    PRINT 'Coluna OrigemSuspeita adicionada a Documentos.';
END
ELSE PRINT 'Documentos.OrigemSuspeita ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'RevisaoHumanaNecessaria')
BEGIN
    ALTER TABLE Documentos ADD RevisaoHumanaNecessaria BIT NOT NULL DEFAULT 0;
    PRINT 'Coluna RevisaoHumanaNecessaria adicionada a Documentos.';
END
ELSE PRINT 'Documentos.RevisaoHumanaNecessaria ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'AtendimentoAgrupadoId')
BEGIN
    ALTER TABLE Documentos ADD AtendimentoAgrupadoId INT NULL;
    -- Adicionar FK somente se AtendimentosAgrupados existir
    IF OBJECT_ID('AtendimentosAgrupados') IS NOT NULL
    BEGIN
        ALTER TABLE Documentos ADD CONSTRAINT FK_Documentos_AtendimentosAgrupados
            FOREIGN KEY (AtendimentoAgrupadoId)
            REFERENCES AtendimentosAgrupados(Id) ON DELETE NO ACTION;
        PRINT 'FK FK_Documentos_AtendimentosAgrupados criada.';
    END
    PRINT 'Coluna AtendimentoAgrupadoId adicionada a Documentos.';
END
ELSE PRINT 'Documentos.AtendimentoAgrupadoId ja existe.';
GO

-- ============================================================
-- Indices adicionais (idempotentes)
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Documentos_ExtendRunId' AND object_id = OBJECT_ID('Documentos'))
BEGIN
    CREATE INDEX IX_Documentos_ExtendRunId ON Documentos(ExtendRunId);
    PRINT 'Indice IX_Documentos_ExtendRunId criado.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AtendimentosAgrupados_NumeroCarteira' AND object_id = OBJECT_ID('AtendimentosAgrupados'))
BEGIN
    CREATE INDEX IX_AtendimentosAgrupados_NumeroCarteira
        ON AtendimentosAgrupados(LoteId, NumeroCarteira);
    PRINT 'Indice IX_AtendimentosAgrupados_NumeroCarteira criado.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Documentos_AtendimentoAgrupadoId' AND object_id = OBJECT_ID('Documentos'))
BEGIN
    CREATE INDEX IX_Documentos_AtendimentoAgrupadoId ON Documentos(AtendimentoAgrupadoId);
    PRINT 'Indice IX_Documentos_AtendimentoAgrupadoId criado.';
END
GO

PRINT '=== Migracao 04_AlterTables_AddMissingColumns concluida com sucesso! ===';
GO
