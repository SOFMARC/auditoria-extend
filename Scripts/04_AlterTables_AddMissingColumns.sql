-- ============================================================
-- Sistema de Auditoria de Pedidos e Guias Medicas
-- Script: 04_AlterTables_AddMissingColumns.sql
-- Versao: 1.0 - Adiciona colunas faltantes para alinhar
--         a estrutura SQL com as entidades C# do dominio
-- Execute APOS o script 01_CreateTables.sql
-- ============================================================

-- ============================================================
-- Tabela: Documentos - Adicionar colunas de integracao Extend
-- ============================================================

-- ExtendFileId: ID do arquivo enviado ao Extend via POST /files/upload
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ExtendFileId')
BEGIN
    ALTER TABLE Documentos ADD ExtendFileId NVARCHAR(200) NULL;
    PRINT 'Coluna ExtendFileId adicionada a Documentos.';
END
ELSE PRINT 'Coluna ExtendFileId ja existe em Documentos.';
GO

-- ExtendRunId: ID do job de extracao (extract_run) retornado pelo Extend
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ExtendRunId')
BEGIN
    ALTER TABLE Documentos ADD ExtendRunId NVARCHAR(200) NULL;
    PRINT 'Coluna ExtendRunId adicionada a Documentos.';
END
ELSE PRINT 'Coluna ExtendRunId ja existe em Documentos.';
GO

-- ExtractorId: ID do extractor configurado na Extend para este tipo de documento
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ExtractorId')
BEGIN
    ALTER TABLE Documentos ADD ExtractorId NVARCHAR(200) NULL;
    PRINT 'Coluna ExtractorId adicionada a Documentos.';
END
ELSE PRINT 'Coluna ExtractorId ja existe em Documentos.';
GO

-- DadosExtraidos: JSON completo retornado pelo webhook da Extend (output.value normalizado)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'DadosExtraidos')
BEGIN
    ALTER TABLE Documentos ADD DadosExtraidos NVARCHAR(MAX) NULL;
    PRINT 'Coluna DadosExtraidos adicionada a Documentos.';
END
ELSE PRINT 'Coluna DadosExtraidos ja existe em Documentos.';
GO

-- ConfiancaOcr: Confianca media de OCR retornada pela Extend (0.0 a 1.0)
-- Renomear ConfiancaOCR para ConfiancaOcr se necessario (EF Core usa ConfiancaOcr)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ConfiancaOcr')
BEGIN
    -- Se a coluna antiga existia como ConfiancaOCR, renomeia
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ConfiancaOCR')
        EXEC sp_rename 'Documentos.ConfiancaOCR', 'ConfiancaOcr', 'COLUMN';
    ELSE
        ALTER TABLE Documentos ADD ConfiancaOcr FLOAT NOT NULL DEFAULT 0.0;
    PRINT 'Coluna ConfiancaOcr configurada em Documentos.';
END
ELSE PRINT 'Coluna ConfiancaOcr ja existe em Documentos.';
GO

-- ReviewAgentScore: Score de revisao do agente Extend (1-5)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'ReviewAgentScore')
BEGIN
    ALTER TABLE Documentos ADD ReviewAgentScore INT NULL;
    PRINT 'Coluna ReviewAgentScore adicionada a Documentos.';
END
ELSE PRINT 'Coluna ReviewAgentScore ja existe em Documentos.';
GO

-- OrigemSuspeita: Indica que o documento tem itens ancorados em paginas impressas posteriores
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'OrigemSuspeita')
BEGIN
    ALTER TABLE Documentos ADD OrigemSuspeita BIT NOT NULL DEFAULT 0;
    PRINT 'Coluna OrigemSuspeita adicionada a Documentos.';
END
ELSE PRINT 'Coluna OrigemSuspeita ja existe em Documentos.';
GO

-- RevisaoHumanaNecessaria: Indica que o documento requer revisao humana obrigatoria
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'RevisaoHumanaNecessaria')
BEGIN
    ALTER TABLE Documentos ADD RevisaoHumanaNecessaria BIT NOT NULL DEFAULT 0;
    PRINT 'Coluna RevisaoHumanaNecessaria adicionada a Documentos.';
END
ELSE PRINT 'Coluna RevisaoHumanaNecessaria ja existe em Documentos.';
GO

-- AtendimentoAgrupadoId: FK para AtendimentosAgrupados
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Documentos') AND name = 'AtendimentoAgrupadoId')
BEGIN
    ALTER TABLE Documentos ADD AtendimentoAgrupadoId INT NULL;
    ALTER TABLE Documentos ADD CONSTRAINT FK_Documentos_Atendimentos
        FOREIGN KEY (AtendimentoAgrupadoId)
        REFERENCES AtendimentosAgrupados(Id) ON DELETE NO ACTION;
    PRINT 'Coluna AtendimentoAgrupadoId adicionada a Documentos.';
END
ELSE PRINT 'Coluna AtendimentoAgrupadoId ja existe em Documentos.';
GO

-- ============================================================
-- Tabela: AtendimentosAgrupados - Adicionar colunas faltantes
-- ============================================================

-- NumeroCarteira: Chave de agrupamento (numero_carteira do paciente)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'NumeroCarteira')
BEGIN
    ALTER TABLE AtendimentosAgrupados ADD NumeroCarteira NVARCHAR(50) NULL;
    PRINT 'Coluna NumeroCarteira adicionada a AtendimentosAgrupados.';
END
ELSE PRINT 'Coluna NumeroCarteira ja existe em AtendimentosAgrupados.';
GO

-- CrmMedico: CRM do medico solicitante
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'CrmMedico')
BEGIN
    ALTER TABLE AtendimentosAgrupados ADD CrmMedico NVARCHAR(50) NULL;
    PRINT 'Coluna CrmMedico adicionada a AtendimentosAgrupados.';
END
ELSE PRINT 'Coluna CrmMedico ja existe em AtendimentosAgrupados.';
GO

-- QuantidadeDocumentos: Contador de documentos no agrupamento
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'QuantidadeDocumentos')
BEGIN
    -- Migrar dados de TotalDocumentos se existir
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'TotalDocumentos')
    BEGIN
        ALTER TABLE AtendimentosAgrupados ADD QuantidadeDocumentos INT NOT NULL DEFAULT 0;
        UPDATE AtendimentosAgrupados SET QuantidadeDocumentos = TotalDocumentos;
        PRINT 'Coluna QuantidadeDocumentos adicionada e dados migrados de TotalDocumentos.';
    END
    ELSE
    BEGIN
        ALTER TABLE AtendimentosAgrupados ADD QuantidadeDocumentos INT NOT NULL DEFAULT 0;
        PRINT 'Coluna QuantidadeDocumentos adicionada a AtendimentosAgrupados.';
    END
END
ELSE PRINT 'Coluna QuantidadeDocumentos ja existe em AtendimentosAgrupados.';
GO

-- QuantidadeDivergencias: Contador de divergencias no agrupamento
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'QuantidadeDivergencias')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'TotalDivergencias')
    BEGIN
        ALTER TABLE AtendimentosAgrupados ADD QuantidadeDivergencias INT NOT NULL DEFAULT 0;
        UPDATE AtendimentosAgrupados SET QuantidadeDivergencias = TotalDivergencias;
        PRINT 'Coluna QuantidadeDivergencias adicionada e dados migrados de TotalDivergencias.';
    END
    ELSE
    BEGIN
        ALTER TABLE AtendimentosAgrupados ADD QuantidadeDivergencias INT NOT NULL DEFAULT 0;
        PRINT 'Coluna QuantidadeDivergencias adicionada a AtendimentosAgrupados.';
    END
END
ELSE PRINT 'Coluna QuantidadeDivergencias ja existe em AtendimentosAgrupados.';
GO

-- QuantidadeRevisaoHumana: Contador de itens que precisam de revisao humana
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'QuantidadeRevisaoHumana')
BEGIN
    ALTER TABLE AtendimentosAgrupados ADD QuantidadeRevisaoHumana INT NOT NULL DEFAULT 0;
    PRINT 'Coluna QuantidadeRevisaoHumana adicionada a AtendimentosAgrupados.';
END
ELSE PRINT 'Coluna QuantidadeRevisaoHumana ja existe em AtendimentosAgrupados.';
GO

-- ScoreRisco: Score de risco de 0 a 100 calculado pela Regra G
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'ScoreRisco')
BEGIN
    ALTER TABLE AtendimentosAgrupados ADD ScoreRisco FLOAT NOT NULL DEFAULT 0.0;
    PRINT 'Coluna ScoreRisco adicionada a AtendimentosAgrupados.';
END
ELSE PRINT 'Coluna ScoreRisco ja existe em AtendimentosAgrupados.';
GO

-- RevisaoHumanaNecessaria: Flag que indica necessidade de revisao humana
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AtendimentosAgrupados') AND name = 'RevisaoHumanaNecessaria')
BEGIN
    ALTER TABLE AtendimentosAgrupados ADD RevisaoHumanaNecessaria BIT NOT NULL DEFAULT 0;
    PRINT 'Coluna RevisaoHumanaNecessaria adicionada a AtendimentosAgrupados.';
END
ELSE PRINT 'Coluna RevisaoHumanaNecessaria ja existe em AtendimentosAgrupados.';
GO

-- ============================================================
-- Tabela: Lotes - Adicionar colunas faltantes
-- ============================================================

-- EnviadosExtend: Contador de documentos enviados ao Extend
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'EnviadosExtend')
BEGIN
    ALTER TABLE Lotes ADD EnviadosExtend INT NOT NULL DEFAULT 0;
    PRINT 'Coluna EnviadosExtend adicionada a Lotes.';
END
ELSE PRINT 'Coluna EnviadosExtend ja existe em Lotes.';
GO

-- TotalDivergencias: Contador total de divergencias no lote
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'TotalDivergencias')
BEGIN
    ALTER TABLE Lotes ADD TotalDivergencias INT NOT NULL DEFAULT 0;
    PRINT 'Coluna TotalDivergencias adicionada a Lotes.';
END
ELSE PRINT 'Coluna TotalDivergencias ja existe em Lotes.';
GO

-- TotalRevisaoHumana: Contador de documentos que precisam de revisao humana
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Lotes') AND name = 'TotalRevisaoHumana')
BEGIN
    ALTER TABLE Lotes ADD TotalRevisaoHumana INT NOT NULL DEFAULT 0;
    PRINT 'Coluna TotalRevisaoHumana adicionada a Lotes.';
END
ELSE PRINT 'Coluna TotalRevisaoHumana ja existe em Lotes.';
GO

-- ============================================================
-- Indices adicionais para performance
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Documentos_ExtendRunId')
BEGIN
    CREATE INDEX IX_Documentos_ExtendRunId ON Documentos(ExtendRunId);
    PRINT 'Indice IX_Documentos_ExtendRunId criado.';
END
ELSE PRINT 'Indice IX_Documentos_ExtendRunId ja existe.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AtendimentosAgrupados_LoteId_NumeroCarteira')
BEGIN
    CREATE INDEX IX_AtendimentosAgrupados_LoteId_NumeroCarteira
        ON AtendimentosAgrupados(LoteId, NumeroCarteira);
    PRINT 'Indice IX_AtendimentosAgrupados_LoteId_NumeroCarteira criado.';
END
ELSE PRINT 'Indice IX_AtendimentosAgrupados_LoteId_NumeroCarteira ja existe.';
GO

PRINT '==========================================================';
PRINT '04_AlterTables_AddMissingColumns.sql concluido com sucesso!';
PRINT '==========================================================';
GO
