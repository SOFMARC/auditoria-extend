-- ============================================================
-- Sistema de Auditoria de Pedidos e Guias Médicas
-- Script: 02_CreateIndexes.sql  |  Versão: 2.0
-- Execute APÓS 01_CreateTables.sql
-- ============================================================

-- Índices na tabela Lotes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Lotes_Status')
    CREATE INDEX IX_Lotes_Status ON Lotes(Status);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Lotes_DataCriacao')
    CREATE INDEX IX_Lotes_DataCriacao ON Lotes(DataCriacao DESC);
GO

-- Índices na tabela Documentos
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Documentos_LoteId')
    CREATE INDEX IX_Documentos_LoteId ON Documentos(LoteId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Documentos_Status')
    CREATE INDEX IX_Documentos_Status ON Documentos(Status);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Documentos_TipoDocumento')
    CREATE INDEX IX_Documentos_TipoDocumento ON Documentos(TipoDocumento);
GO

-- Índices na tabela AtendimentosAgrupados
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Atendimentos_LoteId')
    CREATE INDEX IX_Atendimentos_LoteId ON AtendimentosAgrupados(LoteId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Atendimentos_NumeroGuia')
    CREATE INDEX IX_Atendimentos_NumeroGuia ON AtendimentosAgrupados(NumeroGuia);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Atendimentos_NomePaciente')
    CREATE INDEX IX_Atendimentos_NomePaciente ON AtendimentosAgrupados(NomePaciente);
GO

-- Índices na tabela DivergenciasAuditoria
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Divergencias_DocumentoId')
    CREATE INDEX IX_Divergencias_DocumentoId ON DivergenciasAuditoria(DocumentoId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Divergencias_Status')
    CREATE INDEX IX_Divergencias_Status ON DivergenciasAuditoria(Status);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Divergencias_Severidade')
    CREATE INDEX IX_Divergencias_Severidade ON DivergenciasAuditoria(Severidade DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Divergencias_Tipo')
    CREATE INDEX IX_Divergencias_Tipo ON DivergenciasAuditoria(Tipo);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Divergencias_StatusSeveridade')
    CREATE INDEX IX_Divergencias_StatusSeveridade ON DivergenciasAuditoria(Status, Severidade DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Divergencias_DataCriacao')
    CREATE INDEX IX_Divergencias_DataCriacao ON DivergenciasAuditoria(DataCriacao DESC);
GO

-- Índices na tabela RevisoesHumanas
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Revisoes_DivergenciaId')
    CREATE INDEX IX_Revisoes_DivergenciaId ON RevisoesHumanas(DivergenciaId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Revisoes_NomeAuditor')
    CREATE INDEX IX_Revisoes_NomeAuditor ON RevisoesHumanas(NomeAuditor);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Revisoes_DataRevisao')
    CREATE INDEX IX_Revisoes_DataRevisao ON RevisoesHumanas(DataRevisao DESC);
GO

PRINT '==========================================================';
PRINT '02_CreateIndexes.sql concluido com sucesso!';
PRINT '==========================================================';
GO
