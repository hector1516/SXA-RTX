-- =============================================================
-- SXA-RTX-Sync · RefRemote (SQL Server central)
-- La aplicacion crea las tablas remotas AUTOMATICAMENTE al arrancar:
--   misma estructura que la local + columna OrigenPC al final,
--   PK compuesta (Id, OrigenPC). Este script es solo de REFERENCIA.
-- =============================================================

-- Ejemplo: lo que la app genera para dbo.Registros
-- IF OBJECT_ID(N'dbo.Registros', N'U') IS NULL
-- CREATE TABLE dbo.Registros (
--     Id        int           NOT NULL,
--     Nombre    nvarchar(100) NOT NULL,
--     Cantidad  decimal(10,2) NULL,
--     Fecha     datetime2(7)  NOT NULL,
--     Nota      nvarchar(max) NULL,
--     OrigenPC  nvarchar(64)  NOT NULL CONSTRAINT [DF_SXA_Registros_OrigenDF] DEFAULT (N''),
--     CONSTRAINT [PK_SXA_Registros_PK] PRIMARY KEY ([Id], [OrigenPC])
-- );

-- Columna de origen en tablas remotas ya existentes (si faltara).
IF COL_LENGTH(N'dbo.Registros', N'OrigenPC') IS NULL
    ALTER TABLE dbo.Registros
        ADD OrigenPC nvarchar(64) NOT NULL
            CONSTRAINT [DF_SXA_Registros_OrigenDF] DEFAULT (N'');
GO

-- Catalogo de PCs (opcion 3): maquina que reporta, su tipo (VTi/VTech)
-- y ultimo contacto. La crea la app (DeviceRegistry) y se auto-registra.
IF OBJECT_ID(N'dbo.SXA_PCs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SXA_PCs (
        DeviceId       nvarchar(64)  NOT NULL PRIMARY KEY,
        NombrePC       nvarchar(255) NULL,
        TipoMaquina    nvarchar(32)  NULL,
        Modelo         nvarchar(255) NULL,
        PrimerContacto datetime      NOT NULL,
        UltimoContacto datetime      NOT NULL
    );
END
GO