-- =============================================================
-- SXA-RTX-Sync · RefLocal (SQL Express / LocalDB)
-- La aplicacion crea todo esto AUTOMATICAMENTE al arrancar
-- (SXA_SyncLog + triggers + tabla remota). Este script es solo
-- de REFERENCIA para saber que se genera del lado local.
-- =============================================================

-- Tabla de cola de sincronizacion (sidecar, NO modifica tablas de negocio).
IF OBJECT_ID(N'dbo.SXA_SyncLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SXA_SyncLog (
        Id         bigint IDENTITY(1,1) NOT NULL,
        TableName  nvarchar(255) NOT NULL,
        KeyValue   nvarchar(255) NOT NULL,
        Operation  nchar(1)      NOT NULL DEFAULT N'I',
        Status     int           NOT NULL DEFAULT 0,
        Attempts   int           NOT NULL DEFAULT 0,
        CreatedAt  datetime2     NOT NULL DEFAULT SYSDATETIME(),
        ClaimedAt  datetime2     NULL,
        DoneAt     datetime2     NULL,
        LastError  nvarchar(max) NULL,
        CONSTRAINT [PK_SXA_SyncLog] PRIMARY KEY (Id)
    );
    CREATE INDEX [IX_SXA_SyncLog_status]
        ON dbo.SXA_SyncLog (Status, TableName, CreatedAt) INCLUDE (Id, KeyValue);
END
GO

-- Estados de Status:
--   0 = pendiente   1 = en proceso (reclamada)   2 = sincronizada   -1 = error (con LastError)

-- Trigger por tabla de negocio (ejemplo para dbo.Registros con clave Id).
-- Cubre INSERT y UPDATE; distingue la operacion con CASE sobre `deleted`.
-- La app crea/reemplaza estos triggers sola; aqui se muestra el equivalente manual.
EXEC(N'
CREATE OR ALTER TRIGGER [dbo].[TRG_SXA_Registros_SYNC]
ON [dbo].[Registros] AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [dbo].[SXA_SyncLog] (TableName, KeyValue, Operation)
    SELECT N''dbo.Registros'',
           CONVERT(nvarchar(255), i.[Id]),
           CASE WHEN EXISTS (SELECT 1 FROM deleted d WHERE d.[Id] = i.[Id])
                THEN N''U'' ELSE N''I'' END
    FROM inserted AS i;
END
');
GO