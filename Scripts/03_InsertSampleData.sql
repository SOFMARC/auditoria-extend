-- ============================================================
-- Sistema de Auditoria de Pedidos e Guias Médicas
-- Script: 03_InsertSampleData.sql  |  Versão: 2.0
-- Execute APÓS 01_CreateTables.sql e 02_CreateIndexes.sql
-- ATENÇÃO: Dados de exemplo apenas para ambiente de desenvolvimento/testes
-- ============================================================

SET NOCOUNT ON;

-- ============================================================
-- Lotes de exemplo
-- ============================================================
INSERT INTO Lotes (NomeArquivo, CaminhoArquivo, TamanhoArquivo, Status,
    TotalDocumentos, DocumentosProcessados, DocumentosComErro,
    DataCriacao, DataAtualizacao, DataInicioProcessamento, DataFimProcessamento)
VALUES
    ('lote_jan_2025_001.zip', 'uploads/lote_jan_2025_001.zip', 15728640, 2,
     12, 12, 0, '2025-01-10 08:00:00', '2025-01-10 08:05:00', '2025-01-10 08:00:30', '2025-01-10 08:05:00'),
    ('lote_jan_2025_002.zip', 'uploads/lote_jan_2025_002.zip', 8388608, 2,
     8, 7, 1, '2025-01-15 09:30:00', '2025-01-15 09:33:00', '2025-01-15 09:30:15', '2025-01-15 09:33:00'),
    ('lote_fev_2025_001.zip', 'uploads/lote_fev_2025_001.zip', 20971520, 2,
     20, 20, 0, '2025-02-05 10:00:00', '2025-02-05 10:10:00', '2025-02-05 10:00:20', '2025-02-05 10:10:00'),
    ('lote_fev_2025_002.zip', 'uploads/lote_fev_2025_002.zip', 5242880, 3,
     5, 3, 2, '2025-02-20 14:00:00', '2025-02-20 14:02:00', '2025-02-20 14:00:10', NULL),
    ('lote_mar_2025_001.zip', 'uploads/lote_mar_2025_001.zip', 12582912, 1,
     10, 4, 0, '2025-03-01 11:00:00', '2025-03-01 11:00:00', '2025-03-01 11:00:05', NULL),
    ('lote_mar_2025_002.zip', 'uploads/lote_mar_2025_002.zip', 3145728, 0,
     0, 0, 0, '2025-03-10 16:00:00', '2025-03-10 16:00:00', NULL, NULL);
GO

-- ============================================================
-- Documentos de exemplo (para os Lotes 1 e 3 - concluídos)
-- ============================================================
INSERT INTO Documentos (LoteId, NomeArquivo, CaminhoArquivo, TamanhoArquivo,
    TipoDocumento, Status, ConfiancaOCR, DataCriacao, DataAtualizacao)
VALUES
    -- Lote 1 (Id=1)
    (1, 'guia_sp_001.pdf',    'uploads/1/guia_sp_001.pdf',    1048576, 1, 2, 0.95, '2025-01-10 08:01:00', '2025-01-10 08:02:00'),
    (1, 'guia_sadt_001.pdf',  'uploads/1/guia_sadt_001.pdf',  2097152, 2, 2, 0.88, '2025-01-10 08:01:10', '2025-01-10 08:02:30'),
    (1, 'pedido_med_001.pdf', 'uploads/1/pedido_med_001.pdf', 524288,  3, 2, 0.72, '2025-01-10 08:01:20', '2025-01-10 08:03:00'),
    (1, 'guia_sp_002.pdf',    'uploads/1/guia_sp_002.pdf',    1572864, 1, 2, 0.91, '2025-01-10 08:01:30', '2025-01-10 08:03:30'),
    (1, 'pedido_med_002.pdf', 'uploads/1/pedido_med_002.pdf', 786432,  3, 2, 0.65, '2025-01-10 08:01:40', '2025-01-10 08:04:00'),
    -- Lote 3 (Id=3)
    (3, 'guia_sp_010.pdf',    'uploads/3/guia_sp_010.pdf',    1048576, 1, 2, 0.97, '2025-02-05 10:01:00', '2025-02-05 10:02:00'),
    (3, 'guia_sadt_010.pdf',  'uploads/3/guia_sadt_010.pdf',  2097152, 2, 2, 0.82, '2025-02-05 10:01:10', '2025-02-05 10:03:00'),
    (3, 'pedido_med_010.pdf', 'uploads/3/pedido_med_010.pdf', 524288,  3, 2, 0.70, '2025-02-05 10:01:20', '2025-02-05 10:04:00'),
    (3, 'guia_sp_011.pdf',    'uploads/3/guia_sp_011.pdf',    1572864, 1, 2, 0.93, '2025-02-05 10:01:30', '2025-02-05 10:05:00'),
    (3, 'laudo_001.pdf',      'uploads/3/laudo_001.pdf',      786432,  4, 2, 0.85, '2025-02-05 10:01:40', '2025-02-05 10:06:00');
GO

-- ============================================================
-- Divergências de exemplo
-- ============================================================
INSERT INTO DivergenciasAuditoria (DocumentoId, Tipo, Severidade, Status,
    Descricao, DetalhesTecnicos, CampoAfetado, ValorEncontrado, ValorEsperado, ValorConfianca,
    DataCriacao, DataAtualizacao)
VALUES
    -- Documento 1 (guia_sp_001.pdf)
    (1, 1, 2, 0, 'Confiança OCR abaixo do limiar aceitável',
     'Confiança: 0.95 - Limiar: 0.75 - Página 3 com baixa qualidade de imagem',
     'ConfiancaOCR', '0.95', '>= 0.75', 0.95, '2025-01-10 08:02:30', '2025-01-10 08:02:30'),

    -- Documento 2 (guia_sadt_001.pdf)
    (2, 2, 3, 0, 'Procedimento não encontrado no dicionário TUSS',
     'Código 40301012 não localizado na tabela de procedimentos vigente',
     'CodigoProcedimento', '40301012', 'Código TUSS válido', NULL, '2025-01-10 08:02:40', '2025-01-10 08:02:40'),
    (2, 4, 2, 1, 'Procedimento em página suspeita (página 5+)',
     'Procedimento encontrado na página 6 do documento, fora do padrão esperado',
     'PaginaOrigem', '6', '<= 4', NULL, '2025-01-10 08:02:50', '2025-01-10 08:02:50'),

    -- Documento 3 (pedido_med_001.pdf - confiança 0.72)
    (3, 1, 4, 0, 'Confiança OCR crítica - documento ilegível',
     'Confiança: 0.72 - Abaixo do limiar crítico de 0.75. Documento pode estar danificado.',
     'ConfiancaOCR', '0.72', '>= 0.75', 0.72, '2025-01-10 08:03:10', '2025-01-10 08:03:10'),
    (3, 7, 3, 0, 'Campo obrigatório ausente: CRM do médico',
     'O campo CRM do médico solicitante não foi identificado no documento',
     'CRMMedico', NULL, 'CRM preenchido', NULL, '2025-01-10 08:03:20', '2025-01-10 08:03:20'),

    -- Documento 5 (pedido_med_002.pdf - confiança 0.65)
    (5, 1, 4, 0, 'Confiança OCR crítica - documento ilegível',
     'Confiança: 0.65 - Muito abaixo do limiar mínimo aceitável.',
     'ConfiancaOCR', '0.65', '>= 0.75', 0.65, '2025-01-10 08:04:10', '2025-01-10 08:04:10'),
    (5, 3, 3, 0, 'Possível duplicidade detectada com documento anterior',
     'Hash de conteúdo similar ao documento pedido_med_001.pdf (similaridade: 87%)',
     'HashConteudo', 'abc123', 'Único no lote', NULL, '2025-01-10 08:04:20', '2025-01-10 08:04:20'),

    -- Documento 7 (guia_sadt_010.pdf)
    (7, 2, 2, 2, 'Procedimento com código desatualizado',
     'Código 30721013 foi substituído pelo código 30721021 na tabela TUSS 2024',
     'CodigoProcedimento', '30721013', '30721021', NULL, '2025-02-05 10:03:10', '2025-02-05 10:05:00'),

    -- Documento 8 (pedido_med_010.pdf - confiança 0.70)
    (8, 1, 3, 3, 'Confiança OCR abaixo do limiar',
     'Confiança: 0.70 - Abaixo do limiar de 0.75',
     'ConfiancaOCR', '0.70', '>= 0.75', 0.70, '2025-02-05 10:04:10', '2025-02-05 10:06:00');
GO

-- ============================================================
-- Revisões humanas de exemplo (para divergências já revisadas)
-- ============================================================
INSERT INTO RevisoesHumanas (DivergenciaId, Decisao, NomeAuditor,
    Justificativa, ObservacaoCorrecao, DataRevisao, DataCriacao, DataAtualizacao)
VALUES
    -- Divergência 7 (aceita)
    (7, 2, 'Ana Paula Ferreira',
     'Código desatualizado confirmado. Documento aceito com ressalva.',
     NULL, '2025-02-06 09:00:00', '2025-02-06 09:00:00', '2025-02-06 09:00:00'),
    -- Divergência 8 (rejeitada)
    (8, 3, 'Carlos Eduardo Santos',
     'Documento com qualidade insuficiente. Solicitado reenvio ao prestador.',
     NULL, '2025-02-06 09:30:00', '2025-02-06 09:30:00', '2025-02-06 09:30:00'),
    -- Divergência 4 (em revisão - aceita)
    (4, 2, 'Mariana Costa',
     'Confiança baixa mas conteúdo legível e correto após revisão manual.',
     NULL, '2025-01-11 10:00:00', '2025-01-11 10:00:00', '2025-01-11 10:00:00');
GO

PRINT '==========================================================';
PRINT '03_InsertSampleData.sql concluido com sucesso!';
PRINT 'Dados inseridos: 6 Lotes, 10 Documentos, 9 Divergencias, 3 Revisoes';
PRINT '==========================================================';
GO
