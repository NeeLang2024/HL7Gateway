-- HL7Gateway 增量迁移（SQL Server）
-- 适用：已安装旧版、缺少 PatientLocation 或 Auto ADT 相关表时
-- 在 SSMS 中连接到 HL7Gateway 数据库后执行

USE [HL7Gateway];
GO

-- 1) HL7Messages 增加床位列（消息列表「床位」列、空床 ORU 识别用）
IF COL_LENGTH('dbo.HL7Messages', 'PatientLocation') IS NULL
BEGIN
    ALTER TABLE [dbo].[HL7Messages] ADD [PatientLocation] NVARCHAR(200) NULL;
    PRINT 'Added HL7Messages.PatientLocation';
END
ELSE
    PRINT 'HL7Messages.PatientLocation already exists';
GO

-- 2) Auto ADT 床位映射表
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AutoAdtBeds')
BEGIN
    CREATE TABLE [dbo].[AutoAdtBeds] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [CareArea] NVARCHAR(100) NULL,
        [Room] NVARCHAR(100) NULL,
        [Bed] NVARCHAR(50) NULL,
        [BedLabel] NVARCHAR(100) NULL,
        [DeviceCode] NVARCHAR(100) NULL,
        [DeviceBarcode] NVARCHAR(200) NULL,
        [BedBarcode] NVARCHAR(200) NULL,
        [PhilipsLocationValue] NVARCHAR(300) NOT NULL,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [Remark] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME2 NOT NULL,
        [UpdatedAt] DATETIME2 NOT NULL
    );
    CREATE INDEX [IX_AutoAdtBeds_DeviceCode] ON [dbo].[AutoAdtBeds]([DeviceCode]);
    CREATE INDEX [IX_AutoAdtBeds_PhilipsLocationValue] ON [dbo].[AutoAdtBeds]([PhilipsLocationValue]);
    PRINT 'Created AutoAdtBeds';
END
GO

-- 3) 病人-床位绑定（看板「占用/空闲」来源）
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AutoAdtBindings')
BEGIN
    CREATE TABLE [dbo].[AutoAdtBindings] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [PatientId] NVARCHAR(100) NOT NULL,
        [VisitId] NVARCHAR(100) NOT NULL,
        [BedId] BIGINT NOT NULL,
        [DeviceCode] NVARCHAR(100) NULL,
        [BindingStatus] NVARCHAR(30) NOT NULL,
        [BindTime] DATETIME2 NOT NULL,
        [UnbindTime] DATETIME2 NULL,
        [CreatedAt] DATETIME2 NOT NULL,
        [UpdatedAt] DATETIME2 NOT NULL
    );
    CREATE INDEX [IX_AutoAdtBindings_BedStatus] ON [dbo].[AutoAdtBindings]([BedId], [BindingStatus]);
    PRINT 'Created AutoAdtBindings';
END
GO

-- 4) Auto ADT 事件 / 消息日志
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AutoAdtEvents')
BEGIN
    CREATE TABLE [dbo].[AutoAdtEvents] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [EventType] NVARCHAR(10) NOT NULL,
        [PatientId] NVARCHAR(100) NOT NULL,
        [VisitId] NVARCHAR(100) NOT NULL,
        [SourceBedId] BIGINT NULL,
        [TargetBedId] BIGINT NULL,
        [BindingId] BIGINT NULL,
        [MessageControlId] NVARCHAR(100) NOT NULL,
        [EventStatus] NVARCHAR(30) NOT NULL,
        [OperatorUser] NVARCHAR(100) NULL,
        [PatientSnapshotJson] NVARCHAR(MAX) NULL,
        [BedSnapshotJson] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL,
        [UpdatedAt] DATETIME2 NOT NULL
    );
    PRINT 'Created AutoAdtEvents';
END
ELSE IF COL_LENGTH('dbo.AutoAdtEvents', 'OperatorUser') IS NULL
BEGIN
    ALTER TABLE [dbo].[AutoAdtEvents] ADD [OperatorUser] NVARCHAR(100) NULL;
    PRINT 'Added AutoAdtEvents.OperatorUser';
END
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AutoAdtMessages')
BEGIN
    CREATE TABLE [dbo].[AutoAdtMessages] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [EventId] BIGINT NOT NULL,
        [AdtQueueId] BIGINT NULL,
        [MessageType] NVARCHAR(20) NOT NULL,
        [MessageControlId] NVARCHAR(100) NOT NULL,
        [Hl7Raw] NVARCHAR(MAX) NOT NULL,
        [SendStatus] NVARCHAR(30) NOT NULL,
        [ResponseText] NVARCHAR(MAX) NULL,
        [ErrorText] NVARCHAR(MAX) NULL,
        [RetryCount] INT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL,
        [QueuedAt] DATETIME2 NULL,
        [SentAt] DATETIME2 NULL
    );
    PRINT 'Created AutoAdtMessages';
END
GO

-- 5) 扫码规则
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AutoAdtScanRules')
BEGIN
    CREATE TABLE [dbo].[AutoAdtScanRules] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Name] NVARCHAR(100) NOT NULL,
        [RuleType] NVARCHAR(20) NOT NULL,
        [Pattern] NVARCHAR(500) NULL,
        [StripPrefixes] NVARCHAR(300) NULL,
        [Priority] INT NOT NULL DEFAULT 100,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [Sample] NVARCHAR(200) NULL,
        [Remark] NVARCHAR(300) NULL,
        [CreatedAt] DATETIME2 NOT NULL,
        [UpdatedAt] DATETIME2 NOT NULL
    );
    CREATE INDEX [IX_AutoAdtScanRules_RuleType] ON [dbo].[AutoAdtScanRules]([RuleType]);
    CREATE INDEX [IX_AutoAdtScanRules_TypeEnabledPriority] ON [dbo].[AutoAdtScanRules]([RuleType], [IsEnabled], [Priority]);
    PRINT 'Created AutoAdtScanRules';
END
GO

-- 6) Auto ADT 功能开关（键值对）
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AutoAdtSettings')
BEGIN
    CREATE TABLE [dbo].[AutoAdtSettings] (
        [Key] NVARCHAR(100) NOT NULL PRIMARY KEY,
        [Value] NVARCHAR(MAX) NOT NULL,
        [UpdatedAt] DATETIME2 NOT NULL
    );
    PRINT 'Created AutoAdtSettings';
END
GO

-- 验证
SELECT 'HL7Messages.PatientLocation' AS [Check],
       CASE WHEN COL_LENGTH('dbo.HL7Messages', 'PatientLocation') IS NOT NULL THEN 'OK' ELSE 'MISSING' END AS [Status]
UNION ALL
SELECT 'AutoAdtBeds', CASE WHEN OBJECT_ID('dbo.AutoAdtBeds') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'AutoAdtBindings', CASE WHEN OBJECT_ID('dbo.AutoAdtBindings') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'AutoAdtScanRules', CASE WHEN OBJECT_ID('dbo.AutoAdtScanRules') IS NOT NULL THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'AutoAdtSettings', CASE WHEN OBJECT_ID('dbo.AutoAdtSettings') IS NOT NULL THEN 'OK' ELSE 'MISSING' END;
GO
