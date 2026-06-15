using HL7Gateway.Core;
using HL7Gateway.Core.DbContexts;
using HL7Gateway.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HL7Gateway.Core.Data;

public static class DbInitializer
{
    /// <param name="applySchemaMigrations">
    /// 仅 WebApi 启动时应为 true（建表/加列/种子数据）。
    /// Service 进程设为 false，避免与 WebApi 同时跑 DDL 锁死 SQL Server，导致升级后全站 API 卡顿。
    /// </param>
    public static async Task InitializeAsync(Hl7GatewayDbContext db, bool applySchemaMigrations = true)
    {
        if (applySchemaMigrations)
        {
            try
            {
                await db.Database.EnsureCreatedAsync();
            }
            catch (Exception ex) when (ex.Message.Contains("already exists"))
            {
            }

            await EnsureAutoAdtTablesAsync(db);
            await EnsureMessageColumnsAsync(db);
            await EnsureAutoAdtColumnsAsync(db);
            await EnsureAutoAdtSettingsTableAsync(db);
            await EnsurePerformanceIndexesAsync(db);
            await EnsureIntegrationTraceTableAsync(db);
            await EnsureRoutingRulesTableAsync(db);
        }
        else
        {
            // Service 仅验证数据库可达，不做任何 DDL
            try
            {
                if (!await db.Database.CanConnectAsync())
                    Console.Error.WriteLine("[DbInitializer] Service: database unreachable");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DbInitializer] Service: CanConnect failed: {ex.Message}");
            }
        }

        if (!applySchemaMigrations)
            return;

        try
        {
            var _ = await db.WsiSubscriptions.FirstOrDefaultAsync();
        }
        catch
        {
            try
            {
                var isSqlServer = db.Database.ProviderName?.Contains("SqlServer") == true;
                if (isSqlServer)
                {
                    await db.Database.ExecuteSqlRawAsync(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WsiSubscriptions')
                            CREATE TABLE [dbo].[WsiSubscriptions] (
                                [SubscriptionId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                [NotificationUri] NVARCHAR(500) NOT NULL,
                                [ClientId] NVARCHAR(100) NULL,
                                [PatientIdDomain] NVARCHAR(100) NULL,
                                [FacilityCode] NVARCHAR(500) NULL,
                                [IsActive] BIT NOT NULL DEFAULT 1,
                                [FilterCriteria] NVARCHAR(500) NULL,
                                [CreatedAt] DATETIME2 NOT NULL,
                                [ExpiresAt] DATETIME2 NOT NULL,
                                [LastNotifiedAt] DATETIME2 NULL,
                                [NotifyCount] INT NOT NULL DEFAULT 0,
                                [FailedCount] INT NOT NULL DEFAULT 0,
                                [LastFailedAt] DATETIME2 NULL
                            )");
                }
                else
                {
                    await db.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS [WsiSubscriptions] (
                            [SubscriptionId] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                            [NotificationUri] TEXT NOT NULL,
                            [ClientId] TEXT NULL,
                            [PatientIdDomain] TEXT NULL,
                            [FacilityCode] TEXT NULL,
                            [IsActive] INTEGER NOT NULL DEFAULT 1,
                            [FilterCriteria] TEXT NULL,
                            [CreatedAt] TEXT NOT NULL,
                            [ExpiresAt] TEXT NOT NULL,
                            [LastNotifiedAt] TEXT NULL,
                            [NotifyCount] INTEGER NOT NULL DEFAULT 0,
                            [FailedCount] INTEGER NOT NULL DEFAULT 0,
                            [LastFailedAt] TEXT NULL
                        )");
                }
            }
            catch
            {
            }
        }

        if (!await db.IdentifierMappings.AnyAsync())
        {
            SeedIdentifierMappings(db);
            try { await db.SaveChangesAsync(); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DbInitializer] Seed failed: {ex.Message}");
            }
        }
        else if (await db.IdentifierMappings.CountAsync() < 10)
        {
            await EnsureKnownIdentifierMappingsAsync(db);
        }

        try
        {
            if (!await db.Users.AnyAsync())
            {
                db.Users.Add(new User
                {
                    Username = "admin",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                    DisplayName = "Administrator",
                    Role = "Admin",
                    IsActive = true,
                    CreatedAt = ChinaTime.Now
                });
                try { await db.SaveChangesAsync(); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[DbInitializer] Seed user failed: {ex.Message}");
                }
            }
        }
        catch
        {
            try
            {
                var isSqlServer = db.Database.ProviderName?.Contains("SqlServer") == true;
                if (isSqlServer)
                {
                    await db.Database.ExecuteSqlRawAsync(@"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
                            CREATE TABLE [dbo].[Users] (
                                [UserId] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                [Username] NVARCHAR(50) NOT NULL,
                                [PasswordHash] NVARCHAR(200) NOT NULL,
                                [DisplayName] NVARCHAR(100) NOT NULL,
                                [Role] NVARCHAR(50) NOT NULL DEFAULT 'User',
                                [IsActive] BIT NOT NULL DEFAULT 1,
                                [CreatedAt] DATETIME2 NOT NULL
                            )");
                }
                else
                {
                    await db.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS [Users] (
                            [UserId] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                            [Username] TEXT NOT NULL,
                            [PasswordHash] TEXT NOT NULL,
                            [DisplayName] TEXT NOT NULL,
                            [Role] TEXT NOT NULL DEFAULT 'User',
                            [IsActive] INTEGER NOT NULL DEFAULT 1,
                            [CreatedAt] TEXT NOT NULL
                        )");
                }
                // 表建好后重新插入默认用户
                if (!await db.Users.AnyAsync())
                {
                    db.Users.Add(new User
                    {
                        Username = "admin",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                        DisplayName = "Administrator",
                        Role = "Admin",
                        IsActive = true,
                        CreatedAt = ChinaTime.Now
                    });
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DbInitializer] Create Users table failed: {ex.Message}");
            }
        }
    }

    private static async Task EnsureAutoAdtTablesAsync(Hl7GatewayDbContext db)
    {
        try
        {
            var isSqlServer = db.Database.ProviderName?.Contains("SqlServer") == true;
            if (isSqlServer)
            {
                await db.Database.ExecuteSqlRawAsync(@"
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
    CREATE INDEX [IX_AutoAdtBeds_DeviceBarcode] ON [dbo].[AutoAdtBeds]([DeviceBarcode]);
    CREATE INDEX [IX_AutoAdtBeds_BedBarcode] ON [dbo].[AutoAdtBeds]([BedBarcode]);
    CREATE INDEX [IX_AutoAdtBeds_PhilipsLocationValue] ON [dbo].[AutoAdtBeds]([PhilipsLocationValue]);
    CREATE INDEX [IX_AutoAdtBeds_IsEnabled] ON [dbo].[AutoAdtBeds]([IsEnabled]);
END

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
    CREATE INDEX [IX_AutoAdtBindings_PatientId] ON [dbo].[AutoAdtBindings]([PatientId]);
    CREATE INDEX [IX_AutoAdtBindings_VisitId] ON [dbo].[AutoAdtBindings]([VisitId]);
    CREATE INDEX [IX_AutoAdtBindings_BedId] ON [dbo].[AutoAdtBindings]([BedId]);
    CREATE INDEX [IX_AutoAdtBindings_BindingStatus] ON [dbo].[AutoAdtBindings]([BindingStatus]);
    CREATE INDEX [IX_AutoAdtBindings_BedStatus] ON [dbo].[AutoAdtBindings]([BedId], [BindingStatus]);
    CREATE INDEX [IX_AutoAdtBindings_VisitStatus] ON [dbo].[AutoAdtBindings]([VisitId], [BindingStatus]);
END

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
        [PatientSnapshotJson] NVARCHAR(MAX) NULL,
        [BedSnapshotJson] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL,
        [UpdatedAt] DATETIME2 NOT NULL
    );
    CREATE INDEX [IX_AutoAdtEvents_EventType] ON [dbo].[AutoAdtEvents]([EventType]);
    CREATE INDEX [IX_AutoAdtEvents_PatientId] ON [dbo].[AutoAdtEvents]([PatientId]);
    CREATE INDEX [IX_AutoAdtEvents_VisitId] ON [dbo].[AutoAdtEvents]([VisitId]);
    CREATE INDEX [IX_AutoAdtEvents_MessageControlId] ON [dbo].[AutoAdtEvents]([MessageControlId]);
    CREATE INDEX [IX_AutoAdtEvents_CreatedAt] ON [dbo].[AutoAdtEvents]([CreatedAt]);
END

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
    CREATE INDEX [IX_AutoAdtMessages_EventId] ON [dbo].[AutoAdtMessages]([EventId]);
    CREATE INDEX [IX_AutoAdtMessages_AdtQueueId] ON [dbo].[AutoAdtMessages]([AdtQueueId]);
    CREATE INDEX [IX_AutoAdtMessages_SendStatus] ON [dbo].[AutoAdtMessages]([SendStatus]);
    CREATE INDEX [IX_AutoAdtMessages_MessageControlId] ON [dbo].[AutoAdtMessages]([MessageControlId]);
    CREATE INDEX [IX_AutoAdtMessages_CreatedAt] ON [dbo].[AutoAdtMessages]([CreatedAt]);
END

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
END");
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS [AutoAdtBeds] (
    [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    [CareArea] TEXT NULL,
    [Room] TEXT NULL,
    [Bed] TEXT NULL,
    [BedLabel] TEXT NULL,
    [DeviceCode] TEXT NULL,
    [DeviceBarcode] TEXT NULL,
    [BedBarcode] TEXT NULL,
    [PhilipsLocationValue] TEXT NOT NULL,
    [IsEnabled] INTEGER NOT NULL DEFAULT 1,
    [Remark] TEXT NULL,
    [CreatedAt] TEXT NOT NULL,
    [UpdatedAt] TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS [AutoAdtBindings] (
    [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    [PatientId] TEXT NOT NULL,
    [VisitId] TEXT NOT NULL,
    [BedId] INTEGER NOT NULL,
    [DeviceCode] TEXT NULL,
    [BindingStatus] TEXT NOT NULL,
    [BindTime] TEXT NOT NULL,
    [UnbindTime] TEXT NULL,
    [CreatedAt] TEXT NOT NULL,
    [UpdatedAt] TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS [AutoAdtEvents] (
    [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    [EventType] TEXT NOT NULL,
    [PatientId] TEXT NOT NULL,
    [VisitId] TEXT NOT NULL,
    [SourceBedId] INTEGER NULL,
    [TargetBedId] INTEGER NULL,
    [BindingId] INTEGER NULL,
    [MessageControlId] TEXT NOT NULL,
    [EventStatus] TEXT NOT NULL,
    [PatientSnapshotJson] TEXT NULL,
    [BedSnapshotJson] TEXT NULL,
    [CreatedAt] TEXT NOT NULL,
    [UpdatedAt] TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS [AutoAdtMessages] (
    [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    [EventId] INTEGER NOT NULL,
    [AdtQueueId] INTEGER NULL,
    [MessageType] TEXT NOT NULL,
    [MessageControlId] TEXT NOT NULL,
    [Hl7Raw] TEXT NOT NULL,
    [SendStatus] TEXT NOT NULL,
    [ResponseText] TEXT NULL,
    [ErrorText] TEXT NULL,
    [RetryCount] INTEGER NOT NULL DEFAULT 0,
    [CreatedAt] TEXT NOT NULL,
    [QueuedAt] TEXT NULL,
    [SentAt] TEXT NULL
);
CREATE INDEX IF NOT EXISTS [IX_AutoAdtBeds_DeviceCode] ON [AutoAdtBeds]([DeviceCode]);
CREATE INDEX IF NOT EXISTS [IX_AutoAdtBeds_DeviceBarcode] ON [AutoAdtBeds]([DeviceBarcode]);
CREATE INDEX IF NOT EXISTS [IX_AutoAdtBeds_BedBarcode] ON [AutoAdtBeds]([BedBarcode]);
CREATE INDEX IF NOT EXISTS [IX_AutoAdtBindings_BedStatus] ON [AutoAdtBindings]([BedId], [BindingStatus]);
CREATE INDEX IF NOT EXISTS [IX_AutoAdtBindings_VisitStatus] ON [AutoAdtBindings]([VisitId], [BindingStatus]);
CREATE TABLE IF NOT EXISTS [AutoAdtScanRules] (
    [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    [Name] TEXT NOT NULL,
    [RuleType] TEXT NOT NULL,
    [Pattern] TEXT NULL,
    [StripPrefixes] TEXT NULL,
    [Priority] INTEGER NOT NULL DEFAULT 100,
    [IsEnabled] INTEGER NOT NULL DEFAULT 1,
    [Sample] TEXT NULL,
    [Remark] TEXT NULL,
    [CreatedAt] TEXT NOT NULL,
    [UpdatedAt] TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS [IX_AutoAdtEvents_CreatedAt] ON [AutoAdtEvents]([CreatedAt]);
CREATE INDEX IF NOT EXISTS [IX_AutoAdtMessages_CreatedAt] ON [AutoAdtMessages]([CreatedAt]);
CREATE INDEX IF NOT EXISTS [IX_AutoAdtScanRules_TypeEnabledPriority] ON [AutoAdtScanRules]([RuleType], [IsEnabled], [Priority]);");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DbInitializer] Create AutoADT tables failed: {ex.Message}");
        }
    }

    private static async Task EnsureAutoAdtColumnsAsync(Hl7GatewayDbContext db)
    {
        try
        {
            var isSqlServer = db.Database.ProviderName?.Contains("SqlServer") == true;
            if (isSqlServer)
            {
                await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('dbo.AutoAdtEvents', 'OperatorUser') IS NULL
    ALTER TABLE [dbo].[AutoAdtEvents] ADD [OperatorUser] NVARCHAR(100) NULL;");
            }
            else
            {
                var exists = false;
                var conn = db.Database.GetDbConnection();
                await conn.OpenAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "PRAGMA table_info('AutoAdtEvents');";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        if (string.Equals(reader["name"]?.ToString(), "OperatorUser", StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }
                finally
                {
                    await conn.CloseAsync();
                }

                if (!exists)
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE [AutoAdtEvents] ADD COLUMN [OperatorUser] TEXT NULL;");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DbInitializer] Ensure AutoAdtEvents.OperatorUser column failed: {ex.Message}");
        }
    }

    private static async Task EnsureAutoAdtSettingsTableAsync(Hl7GatewayDbContext db)
    {
        try
        {
            var isSqlServer = db.Database.ProviderName?.Contains("SqlServer") == true;
            if (isSqlServer)
            {
                await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AutoAdtSettings')
BEGIN
    CREATE TABLE [dbo].[AutoAdtSettings] (
        [Key] NVARCHAR(100) NOT NULL PRIMARY KEY,
        [Value] NVARCHAR(MAX) NOT NULL,
        [UpdatedAt] DATETIME2 NOT NULL
    );
END");
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS [AutoAdtSettings] (
    [Key] TEXT NOT NULL PRIMARY KEY,
    [Value] TEXT NOT NULL,
    [UpdatedAt] TEXT NOT NULL
);");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DbInitializer] Ensure AutoAdtSettings table failed: {ex.Message}");
        }
    }

    private static async Task EnsureMessageColumnsAsync(Hl7GatewayDbContext db)
    {
        try
        {
            var isSqlServer = db.Database.ProviderName?.Contains("SqlServer") == true;
            if (isSqlServer)
            {
                // 用 OBJECT_ID + COL_LENGTH 双重判断，避免表名/架构写法差异导致迁移被跳过
                await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.HL7Messages', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.HL7Messages', N'PatientLocation') IS NULL
BEGIN
    ALTER TABLE [dbo].[HL7Messages] ADD [PatientLocation] NVARCHAR(200) NULL;
END");
            }
            else
            {
                // SQLite: ADD COLUMN fails if the column already exists, so guard with PRAGMA.
                var exists = false;
                var conn = db.Database.GetDbConnection();
                await conn.OpenAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "PRAGMA table_info('HL7Messages');";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        if (string.Equals(reader["name"]?.ToString(), "PatientLocation", StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }
                finally
                {
                    await conn.CloseAsync();
                }

                if (!exists)
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE [HL7Messages] ADD COLUMN [PatientLocation] TEXT NULL;");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DbInitializer] Ensure HL7Messages.PatientLocation column failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 生产库若由 EnsureCreated 早期创建，可能缺少 EF 模型里后来加的索引，导致 HL7Messages 全表 COUNT/排序极慢。
    /// </summary>
    private static async Task EnsurePerformanceIndexesAsync(Hl7GatewayDbContext db)
    {
        try
        {
            var isSqlServer = db.Database.ProviderName?.Contains("SqlServer") == true;
            if (isSqlServer)
            {
                await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.HL7Messages', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HL7Messages_ReceivedAt_ParseStatus' AND object_id = OBJECT_ID(N'dbo.HL7Messages'))
        CREATE NONCLUSTERED INDEX [IX_HL7Messages_ReceivedAt_ParseStatus] ON [dbo].[HL7Messages]([ReceivedAt] DESC, [ParseStatus]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HL7Messages_ParseStatus_ReceivedAt' AND object_id = OBJECT_ID(N'dbo.HL7Messages'))
        CREATE NONCLUSTERED INDEX [IX_HL7Messages_ParseStatus_ReceivedAt] ON [dbo].[HL7Messages]([ParseStatus], [ReceivedAt] DESC);
END
IF OBJECT_ID(N'dbo.SystemLogs', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SystemLogs_CreatedAt' AND object_id = OBJECT_ID(N'dbo.SystemLogs'))
        CREATE NONCLUSTERED INDEX [IX_SystemLogs_CreatedAt] ON [dbo].[SystemLogs]([CreatedAt] DESC);
END
IF OBJECT_ID(N'dbo.AdtQueue', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AdtQueue_Status_CreatedAt' AND object_id = OBJECT_ID(N'dbo.AdtQueue'))
        CREATE NONCLUSTERED INDEX [IX_AdtQueue_Status_CreatedAt] ON [dbo].[AdtQueue]([Status], [CreatedAt] DESC);
END");
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync(@"
CREATE INDEX IF NOT EXISTS [IX_HL7Messages_ReceivedAt_ParseStatus] ON [HL7Messages]([ReceivedAt], [ParseStatus]);
CREATE INDEX IF NOT EXISTS [IX_HL7Messages_ParseStatus_ReceivedAt] ON [HL7Messages]([ParseStatus], [ReceivedAt]);
CREATE INDEX IF NOT EXISTS [IX_SystemLogs_CreatedAt] ON [SystemLogs]([CreatedAt]);
CREATE INDEX IF NOT EXISTS [IX_AdtQueue_Status_CreatedAt] ON [AdtQueue]([Status], [CreatedAt]);");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DbInitializer] Ensure performance indexes failed: {ex.Message}");
        }
    }

    private static async Task EnsureIntegrationTraceTableAsync(Hl7GatewayDbContext db)
    {
        try
        {
            var isSqlServer = db.Database.ProviderName?.Contains("SqlServer") == true;
            if (isSqlServer)
            {
                await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'IntegrationTraceEvents')
BEGIN
    CREATE TABLE [dbo].[IntegrationTraceEvents] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [TraceId] NVARCHAR(100) NOT NULL,
        [Step] NVARCHAR(80) NOT NULL,
        [Category] NVARCHAR(40) NOT NULL,
        [Status] NVARCHAR(20) NOT NULL,
        [PartnerKey] NVARCHAR(80) NULL,
        [Detail] NVARCHAR(2000) NULL,
        [DurationMs] INT NULL,
        [RelatedEntityType] NVARCHAR(40) NULL,
        [RelatedEntityId] BIGINT NULL,
        [CreatedAt] DATETIME2 NOT NULL
    );
    CREATE INDEX [IX_IntegrationTraceEvents_TraceId] ON [dbo].[IntegrationTraceEvents]([TraceId]);
    CREATE INDEX [IX_IntegrationTraceEvents_CreatedAt] ON [dbo].[IntegrationTraceEvents]([CreatedAt]);
    CREATE INDEX [IX_IntegrationTraceEvents_TraceId_CreatedAt] ON [dbo].[IntegrationTraceEvents]([TraceId], [CreatedAt]);
END");
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS [IntegrationTraceEvents] (
    [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    [TraceId] TEXT NOT NULL,
    [Step] TEXT NOT NULL,
    [Category] TEXT NOT NULL,
    [Status] TEXT NOT NULL,
    [PartnerKey] TEXT NULL,
    [Detail] TEXT NULL,
    [DurationMs] INTEGER NULL,
    [RelatedEntityType] TEXT NULL,
    [RelatedEntityId] INTEGER NULL,
    [CreatedAt] TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS [IX_IntegrationTraceEvents_TraceId] ON [IntegrationTraceEvents]([TraceId]);
CREATE INDEX IF NOT EXISTS [IX_IntegrationTraceEvents_CreatedAt] ON [IntegrationTraceEvents]([CreatedAt]);
CREATE INDEX IF NOT EXISTS [IX_IntegrationTraceEvents_TraceId_CreatedAt] ON [IntegrationTraceEvents]([TraceId], [CreatedAt]);");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DbInitializer] Ensure IntegrationTraceEvents table failed: {ex.Message}");
        }
    }

    private static async Task EnsureRoutingRulesTableAsync(Hl7GatewayDbContext db)
    {
        try
        {
            var isSqlServer = db.Database.ProviderName?.Contains("SqlServer") == true;
            if (isSqlServer)
            {
                await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'RoutingRules')
BEGIN
    CREATE TABLE [dbo].[RoutingRules] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Name] NVARCHAR(100) NOT NULL,
        [Priority] INT NOT NULL DEFAULT 100,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [MessageType] NVARCHAR(20) NULL,
        [TriggerEvent] NVARCHAR(20) NULL,
        [SourceIpPattern] NVARCHAR(100) NULL,
        [SendingApp] NVARCHAR(100) NULL,
        [SendingFacility] NVARCHAR(100) NULL,
        [Action] NVARCHAR(40) NOT NULL DEFAULT 'ForwardAdt',
        [WebhookUrl] NVARCHAR(500) NULL,
        [TransformJson] NVARCHAR(MAX) NULL,
        [Remark] NVARCHAR(300) NULL,
        [CreatedAt] DATETIME2 NOT NULL,
        [UpdatedAt] DATETIME2 NOT NULL
    );
    CREATE INDEX [IX_RoutingRules_IsEnabled] ON [dbo].[RoutingRules]([IsEnabled]);
    CREATE INDEX [IX_RoutingRules_IsEnabled_Priority] ON [dbo].[RoutingRules]([IsEnabled], [Priority]);
END");
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS [RoutingRules] (
    [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    [Name] TEXT NOT NULL,
    [Priority] INTEGER NOT NULL DEFAULT 100,
    [IsEnabled] INTEGER NOT NULL DEFAULT 1,
    [MessageType] TEXT NULL,
    [TriggerEvent] TEXT NULL,
    [SourceIpPattern] TEXT NULL,
    [SendingApp] TEXT NULL,
    [SendingFacility] TEXT NULL,
    [Action] TEXT NOT NULL DEFAULT 'ForwardAdt',
    [WebhookUrl] TEXT NULL,
    [TransformJson] TEXT NULL,
    [Remark] TEXT NULL,
    [CreatedAt] TEXT NOT NULL,
    [UpdatedAt] TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS [IX_RoutingRules_IsEnabled] ON [RoutingRules]([IsEnabled]);
CREATE INDEX IF NOT EXISTS [IX_RoutingRules_IsEnabled_Priority] ON [RoutingRules]([IsEnabled], [Priority]);");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DbInitializer] Ensure RoutingRules table failed: {ex.Message}");
        }
    }

    private static void SeedIdentifierMappings(Hl7GatewayDbContext db)
    {
        var now = ChinaTime.Now;

        db.IdentifierMappings.AddRange(
            // Philips MDIL codes
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4182", SourceText = "HR", VitalSignType = "HR", VitalSignName = "心率", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4bb8", SourceText = "SpO2", VitalSignType = "SPO2", VitalSignName = "血氧饱和度", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4bb0", SourceText = "Perf", VitalSignType = "PERF", VitalSignName = "灌注指数", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4822", SourceText = "Pulse (SpO2)", VitalSignType = "PULSE", VitalSignName = "脉搏(SpO2)", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-500a", SourceText = "RR", VitalSignType = "RESP", VitalSignName = "呼吸频率", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-5000", SourceText = "Resp", VitalSignType = "RESP", VitalSignName = "呼吸频率", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4a05", SourceText = "NBPs", VitalSignType = "NIBP_SYS", VitalSignName = "无创收缩压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4a06", SourceText = "NBPd", VitalSignType = "NIBP_DIA", VitalSignName = "无创舒张压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4a07", SourceText = "NBPm", VitalSignType = "NIBP_MEAN", VitalSignName = "无创平均压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-f0e5", SourceText = "Pulse (NBP)", VitalSignType = "PULSE", VitalSignName = "脉搏(NBP)", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4a15", SourceText = "ABPs", VitalSignType = "IBP_SYS", VitalSignName = "有创收缩压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4a16", SourceText = "ABPd", VitalSignType = "IBP_DIA", VitalSignName = "有创舒张压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4a17", SourceText = "ABPm", VitalSignType = "IBP_MEAN", VitalSignName = "有创平均压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4a1d", SourceText = "PAPs", VitalSignType = "PAP_SYS", VitalSignName = "肺动脉收缩压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4a1e", SourceText = "PAPd", VitalSignType = "PAP_DIA", VitalSignName = "肺动脉舒张压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4a1f", SourceText = "PAPm", VitalSignType = "PAP_MEAN", VitalSignName = "肺动脉平均压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-f0c7", SourceText = "T1", VitalSignType = "TEMP", VitalSignName = "体温", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-580b", SourceText = "ICPm", VitalSignType = "ICP", VitalSignName = "颅内压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-5804", SourceText = "CPP", VitalSignType = "CPP", VitalSignName = "脑灌注压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-f828", SourceText = "SpRR", VitalSignType = "RESP", VitalSignName = "呼吸频率(SpRR)", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-3f20", SourceText = "QT", VitalSignType = "QT", VitalSignName = "QT间期", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-3f24", SourceText = "QTc", VitalSignType = "QTC", VitalSignName = "校正QT间期", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-4261", SourceText = "PVC", VitalSignType = "PVC", VitalSignName = "室性早搏", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0401-0068", SourceText = "YSI", VitalSignType = "TEMP", VitalSignName = "体温", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDIL", SourceCode = "0002-5164", SourceText = "O2", VitalSignType = "O2", VitalSignName = "氧气浓度", CreatedAt = now, UpdatedAt = now },

            // IEEE 11073 MDC codes (PIC iX standard)
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "147842", SourceText = "MDC_ECG_HEART_RATE", VitalSignType = "HR", VitalSignName = "心率", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "147618", SourceText = "MDC_PULS_OXIM_SAT_O2", VitalSignType = "SPO2", VitalSignName = "血氧饱和度", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "150456", SourceText = "MDC_PULS_OXIM_SAT_O2", VitalSignType = "SPO2", VitalSignName = "血氧饱和度", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "147844", SourceText = "MDC_PULS_OXIM_PULS_RATE", VitalSignType = "PULSE", VitalSignName = "脉搏", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "149522", SourceText = "MDC_BLD_PULS_RATE_INV", VitalSignType = "PULSE", VitalSignName = "脉搏", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "149530", SourceText = "MDC_PULS_OXIM_PULS_RATE", VitalSignType = "PULSE", VitalSignName = "脉搏", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "147602", SourceText = "MDC_VENT_RESP_RATE", VitalSignType = "RESP", VitalSignName = "呼吸频率", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "151562", SourceText = "MDC_RESP_RATE", VitalSignType = "RESP", VitalSignName = "呼吸频率", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "149514", SourceText = "MDC_NONINV_SYS_PRES", VitalSignType = "NIBP_SYS", VitalSignName = "无创收缩压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "149515", SourceText = "MDC_NONINV_DIA_PRES", VitalSignType = "NIBP_DIA", VitalSignName = "无创舒张压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "149516", SourceText = "MDC_NONINV_MEAN_PRES", VitalSignType = "NIBP_MEAN", VitalSignName = "无创平均压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "150033", SourceText = "MDC_INV_SYS_PRES", VitalSignType = "IBP_SYS", VitalSignName = "有创收缩压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "150034", SourceText = "MDC_INV_DIA_PRES", VitalSignType = "IBP_DIA", VitalSignName = "有创舒张压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "150335", SourceText = "MDC_INV_MEAN_PRES", VitalSignType = "IBP_MEAN", VitalSignName = "有创平均压", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "150043", SourceText = "MDC_TEMP", VitalSignType = "TEMP", VitalSignName = "体温", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "150124", SourceText = "MDC_ETCO2", VitalSignType = "ETCO2", VitalSignName = "呼气末CO2", CreatedAt = now, UpdatedAt = now },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "150044", SourceText = "MDC_CVP", VitalSignType = "CVP", VitalSignName = "中心静脉压", CreatedAt = now, UpdatedAt = now }
        );
    }

    private static async Task EnsureKnownIdentifierMappingsAsync(Hl7GatewayDbContext db)
    {
        var now = ChinaTime.Now;
        var known = new[]
        {
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "147842", SourceText = "MDC_ECG_HEART_RATE", VitalSignType = "HR", VitalSignName = "心率", LoincCode = "8867-4" },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "150456", SourceText = "MDC_PULS_OXIM_SAT_O2", VitalSignType = "SPO2", VitalSignName = "血氧饱和度", LoincCode = "2708-6" },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "151562", SourceText = "MDC_RESP_RATE", VitalSignType = "RESP", VitalSignName = "呼吸频率", LoincCode = "9279-1" },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "149522", SourceText = "MDC_BLD_PULS_RATE_INV", VitalSignType = "PULSE", VitalSignName = "脉搏" },
            new IdentifierMapping { SourceSystem = "MDC", SourceCode = "149530", SourceText = "MDC_PULS_OXIM_PULS_RATE", VitalSignType = "PULSE", VitalSignName = "脉搏" },
        };

        foreach (var mapping in known)
        {
            var exists = await db.IdentifierMappings
                .AnyAsync(m => m.SourceCode == mapping.SourceCode && m.VitalSignType == mapping.VitalSignType);
            if (exists) continue;

            mapping.CreatedAt = now;
            mapping.UpdatedAt = now;
            mapping.IsActive = true;
            db.IdentifierMappings.Add(mapping);
        }

        var arterialPressureMappings = await db.IdentifierMappings
            .Where(m => (m.SourceCode == "150033" || m.SourceCode == "150034" || m.SourceCode == "150035")
                && m.SourceText != null
                && m.SourceText.Contains("ART")
                && m.VitalSignType.StartsWith("NIBP"))
            .ToListAsync();

        foreach (var mapping in arterialPressureMappings)
        {
            mapping.VitalSignType = mapping.SourceCode switch
            {
                "150033" => "IBP_SYS",
                "150034" => "IBP_DIA",
                "150035" => "IBP_MEAN",
                _ => mapping.VitalSignType
            };
            mapping.VitalSignName = mapping.SourceCode switch
            {
                "150033" => "有创收缩压",
                "150034" => "有创舒张压",
                "150035" => "有创平均压",
                _ => mapping.VitalSignName
            };
            mapping.UpdatedAt = now;
        }

        try { await db.SaveChangesAsync(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DbInitializer] Ensure mappings failed: {ex.Message}");
        }
    }
}
