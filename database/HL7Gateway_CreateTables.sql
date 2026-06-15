USE [HL7Gateway];
GO

-- ==================== 建表 11 张 ====================

-- 1. Patients
CREATE TABLE [dbo].[Patients] (
    [PatientId]       NVARCHAR(100)   NOT NULL,
    [PatientIdList]   NVARCHAR(500)   NULL,
    [Name]            NVARCHAR(100)   NULL,
    [DateOfBirth]     DATE            NULL,
    [Gender]          CHAR(1)         NULL,
    [Address]         NVARCHAR(500)   NULL,
    [PhoneNumber]     NVARCHAR(50)    NULL,
    [Ssn]             NVARCHAR(50)    NULL,
    [Race]            NVARCHAR(50)    NULL,
    [MaritalStatus]   NVARCHAR(50)    NULL,
    [CreatedAt]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Patients] PRIMARY KEY CLUSTERED ([PatientId])
);
GO

-- 2. Visits
CREATE TABLE [dbo].[Visits] (
    [VisitId]         NVARCHAR(100)   NOT NULL,
    [PatientId]       NVARCHAR(100)   NOT NULL,
    [AdmitDateTime]   DATETIME2       NULL,
    [DischargeDateTime] DATETIME2     NULL,
    [PatientClass]    NVARCHAR(50)    NULL,
    [AdmitDiagnosis]  NVARCHAR(500)   NULL,
    [AttendingDoctor] NVARCHAR(100)   NULL,
    [ReferringDoctor] NVARCHAR(100)   NULL,
    [Department]      NVARCHAR(100)   NULL,
    [Ward]            NVARCHAR(100)   NULL,
    [Room]            NVARCHAR(50)    NULL,
    [Bed]             NVARCHAR(50)    NULL,
    [PatientType]     NVARCHAR(50)    NULL,
    [CreatedAt]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Visits] PRIMARY KEY CLUSTERED ([VisitId]),
    CONSTRAINT [FK_Visits_Patients] FOREIGN KEY ([PatientId])
        REFERENCES [dbo].[Patients]([PatientId])
);
CREATE INDEX [IX_Visits_PatientId] ON [dbo].[Visits] ([PatientId]);
CREATE INDEX [IX_Visits_AdmitDateTime] ON [dbo].[Visits] ([AdmitDateTime]);
GO

-- 3. HL7Messages
CREATE TABLE [dbo].[HL7Messages] (
    [MessageId]        BIGINT IDENTITY(1,1) NOT NULL,
    [MessageControlId] NVARCHAR(100)  NOT NULL,
    [MessageType]      NVARCHAR(20)   NOT NULL,
    [TriggerEvent]     NVARCHAR(20)   NULL,
    [VersionId]        NVARCHAR(10)   NULL,
    [SendingApp]       NVARCHAR(100)  NULL,
    [SendingFacility]  NVARCHAR(100)  NULL,
    [ReceivingApp]     NVARCHAR(100)  NULL,
    [ReceivingFacility] NVARCHAR(100) NULL,
    [MessageDateTime]  DATETIME2      NULL,
    [SourceIp]         NVARCHAR(50)   NOT NULL,
    [SourcePort]       INT            NOT NULL,
    [RawContent]       NVARCHAR(MAX)  NOT NULL,
    [ParseStatus]      TINYINT        NOT NULL DEFAULT 0,
    [PatientId]        NVARCHAR(100)  NULL,
    [VisitId]          NVARCHAR(100)  NULL,
    [ReceivedAt]       DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    [ProcessedAt]      DATETIME2      NULL,
    [ErrorMessage]     NVARCHAR(2000) NULL,
    CONSTRAINT [PK_HL7Messages] PRIMARY KEY CLUSTERED ([MessageId]),
    CONSTRAINT [UQ_HL7Messages_ControlId] UNIQUE ([MessageControlId])
);
CREATE INDEX [IX_HL7Messages_MessageType] ON [dbo].[HL7Messages] ([MessageType]);
CREATE INDEX [IX_HL7Messages_PatientId] ON [dbo].[HL7Messages] ([PatientId]);
CREATE INDEX [IX_HL7Messages_ReceivedAt] ON [dbo].[HL7Messages] ([ReceivedAt]);
CREATE INDEX [IX_HL7Messages_SourceIp] ON [dbo].[HL7Messages] ([SourceIp]);
GO

-- 4. ParsedSegments
CREATE TABLE [dbo].[ParsedSegments] (
    [SegmentId]       BIGINT IDENTITY(1,1) NOT NULL,
    [MessageId]       BIGINT          NOT NULL,
    [SegmentType]     NVARCHAR(10)    NOT NULL,
    [SegmentIndex]    INT             NOT NULL,
    [SegmentRaw]      NVARCHAR(4000)  NULL,
    [JsonContent]     NVARCHAR(MAX)   NULL,
    CONSTRAINT [PK_ParsedSegments] PRIMARY KEY CLUSTERED ([SegmentId]),
    CONSTRAINT [FK_ParsedSegments_HL7Messages] FOREIGN KEY ([MessageId])
        REFERENCES [dbo].[HL7Messages]([MessageId]) ON DELETE CASCADE
);
CREATE INDEX [IX_ParsedSegments_MessageId] ON [dbo].[ParsedSegments] ([MessageId]);
CREATE INDEX [IX_ParsedSegments_SegmentType] ON [dbo].[ParsedSegments] ([SegmentType]);
GO

-- 5. Observations
CREATE TABLE [dbo].[Observations] (
    [ObservationId]     BIGINT IDENTITY(1,1) NOT NULL,
    [MessageId]         BIGINT          NOT NULL,
    [PatientId]         NVARCHAR(100)   NOT NULL,
    [SetId]             NVARCHAR(10)    NULL,
    [ValueType]         NVARCHAR(10)    NULL,
    [IdentifierCode]    NVARCHAR(100)   NOT NULL,
    [IdentifierText]    NVARCHAR(200)   NULL,
    [IdentifierSystem]  NVARCHAR(100)   NULL,
    [ObservationValue]  NVARCHAR(2000)  NULL,
    [Units]             NVARCHAR(100)   NULL,
    [ReferenceRange]    NVARCHAR(200)   NULL,
    [AbnormalFlags]     NVARCHAR(20)    NULL,
    [ObservationDateTime] DATETIME2     NULL,
    [ProducerId]        NVARCHAR(100)  NULL,
    [ObserveStatus]     NVARCHAR(20)   NULL,
    [CreatedAt]         DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_Observations] PRIMARY KEY CLUSTERED ([ObservationId]),
    CONSTRAINT [FK_Observations_HL7Messages] FOREIGN KEY ([MessageId])
        REFERENCES [dbo].[HL7Messages]([MessageId]) ON DELETE CASCADE
);
CREATE INDEX [IX_Observations_MessageId] ON [dbo].[Observations] ([MessageId]);
CREATE INDEX [IX_Observations_PatientId] ON [dbo].[Observations] ([PatientId]);
CREATE INDEX [IX_Observations_IdentifierCode] ON [dbo].[Observations] ([IdentifierCode]);
CREATE INDEX [IX_Observations_ObsDateTime] ON [dbo].[Observations] ([ObservationDateTime]);
CREATE INDEX [IX_Observations_IdentifierText] ON [dbo].[Observations] ([IdentifierText]);
GO

-- 6. VitalSigns
CREATE TABLE [dbo].[VitalSigns] (
    [VitalSignId]          BIGINT IDENTITY(1,1) NOT NULL,
    [MessageId]            BIGINT          NOT NULL,
    [ObservationId]        BIGINT          NULL,
    [PatientId]            NVARCHAR(100)   NOT NULL,
    [VisitId]              NVARCHAR(100)   NULL,
    [VitalSignType]        NVARCHAR(50)    NOT NULL,
    [VitalSignName]        NVARCHAR(100)   NOT NULL,
    [ValueNumeric]         DECIMAL(18,4)   NULL,
    [ValueString]          NVARCHAR(200)   NULL,
    [Units]                NVARCHAR(50)    NULL,
    [Systolic]             DECIMAL(18,4)   NULL,
    [Diastolic]            DECIMAL(18,4)   NULL,
    [MeanPressure]         DECIMAL(18,4)   NULL,
    [OriginalCode]         NVARCHAR(100)   NOT NULL,
    [OriginalText]         NVARCHAR(200)   NULL,
    [OriginalSystem]       NVARCHAR(100)   NULL,
    [AbnormalFlags]        NVARCHAR(20)    NULL,
    [ReferenceRange]       NVARCHAR(200)   NULL,
    [ObserveStatus]        NVARCHAR(20)    NULL,
    [ObservationDateTime]  DATETIME2       NOT NULL,
    [ReceivedAt]           DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    [DeviceId]             NVARCHAR(100)   NULL,
    CONSTRAINT [PK_VitalSigns] PRIMARY KEY CLUSTERED ([VitalSignId]),
    CONSTRAINT [FK_VitalSigns_HL7Messages] FOREIGN KEY ([MessageId])
        REFERENCES [dbo].[HL7Messages]([MessageId])
);
CREATE INDEX [IX_VitalSigns_PatientId] ON [dbo].[VitalSigns] ([PatientId]);
CREATE INDEX [IX_VitalSigns_VitalSignType] ON [dbo].[VitalSigns] ([VitalSignType]);
CREATE INDEX [IX_VitalSigns_ObservationDateTime] ON [dbo].[VitalSigns] ([ObservationDateTime]);
CREATE INDEX [IX_VitalSigns_PatientId_Type_Time] ON [dbo].[VitalSigns] ([PatientId], [VitalSignType], [ObservationDateTime]);
GO

-- 7. IdentifierMappings
CREATE TABLE [dbo].[IdentifierMappings] (
    [MappingId]       INT IDENTITY(1,1) NOT NULL,
    [SourceSystem]    NVARCHAR(100)   NOT NULL,
    [SourceCode]      NVARCHAR(100)   NOT NULL,
    [SourceText]      NVARCHAR(200)   NULL,
    [VitalSignType]   NVARCHAR(50)    NOT NULL,
    [VitalSignName]   NVARCHAR(100)   NOT NULL,
    [LoincCode]       NVARCHAR(20)    NULL,
    [IsActive]        BIT             NOT NULL DEFAULT 1,
    [CreatedAt]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_IdentifierMappings] PRIMARY KEY CLUSTERED ([MappingId]),
    CONSTRAINT [UQ_IdentifierMappings_Source] UNIQUE ([SourceSystem], [SourceCode])
);
CREATE INDEX [IX_IdentifierMappings_VitalSignType] ON [dbo].[IdentifierMappings] ([VitalSignType]);
GO

-- 8. ADTQueue
CREATE TABLE [dbo].[ADTQueue] (
    [QueueId]         BIGINT IDENTITY(1,1) NOT NULL,
    [MessageId]       BIGINT          NULL,
    [AdtMessageType]  NVARCHAR(20)    NOT NULL,
    [Priority]        INT             NOT NULL DEFAULT 0,
    [MessageContent]  NVARCHAR(MAX)   NOT NULL,
    [TargetEndpoint]  NVARCHAR(500)   NOT NULL,
    [Status]          TINYINT         NOT NULL DEFAULT 0,
    [RetryCount]      INT             NOT NULL DEFAULT 0,
    [MaxRetries]      INT             NOT NULL DEFAULT 3,
    [LastError]       NVARCHAR(2000)  NULL,
    [CreatedAt]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    [SentAt]          DATETIME2       NULL,
    [AckReceivedAt]   DATETIME2       NULL,
    [AckContent]      NVARCHAR(MAX)   NULL,
    CONSTRAINT [PK_ADTQueue] PRIMARY KEY CLUSTERED ([QueueId])
);
CREATE INDEX [IX_ADTQueue_Status] ON [dbo].[ADTQueue] ([Status]);
CREATE INDEX [IX_ADTQueue_CreatedAt] ON [dbo].[ADTQueue] ([CreatedAt]);
GO

-- 9. ADTLog
CREATE TABLE [dbo].[ADTLog] (
    [LogId]           BIGINT IDENTITY(1,1) NOT NULL,
    [QueueId]         BIGINT          NULL,
    [MessageType]     NVARCHAR(20)    NOT NULL,
    [PatientId]       NVARCHAR(100)   NULL,
    [TargetEndpoint]  NVARCHAR(500)   NOT NULL,
    [Status]          TINYINT         NOT NULL,
    [RequestContent]  NVARCHAR(MAX)   NULL,
    [ResponseContent] NVARCHAR(MAX)   NULL,
    [ErrorMessage]    NVARCHAR(2000)  NULL,
    [DurationMs]      INT             NULL,
    [CreatedAt]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ADTLog] PRIMARY KEY CLUSTERED ([LogId])
);
CREATE INDEX [IX_ADTLog_CreatedAt] ON [dbo].[ADTLog] ([CreatedAt]);
CREATE INDEX [IX_ADTLog_PatientId] ON [dbo].[ADTLog] ([PatientId]);
GO

-- 10. DeviceConnections
CREATE TABLE [dbo].[DeviceConnections] (
    [ConnectionId]    BIGINT IDENTITY(1,1) NOT NULL,
    [SourceIp]        NVARCHAR(50)    NOT NULL,
    [SourcePort]      INT             NOT NULL,
    [DeviceName]      NVARCHAR(200)   NULL,
    [MessageCount]    INT             NOT NULL DEFAULT 0,
    [IsConnected]     BIT             NOT NULL DEFAULT 0,
    [FirstConnected]  DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    [LastActivity]    DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    [DisconnectedAt]  DATETIME2       NULL,
    CONSTRAINT [PK_DeviceConnections] PRIMARY KEY CLUSTERED ([ConnectionId])
);
CREATE INDEX [IX_DeviceConnections_SourceIp] ON [dbo].[DeviceConnections] ([SourceIp]);
GO

-- 11. SystemLogs
CREATE TABLE [dbo].[SystemLogs] (
    [LogId]           BIGINT IDENTITY(1,1) NOT NULL,
    [Level]           TINYINT         NOT NULL,
    [Category]        NVARCHAR(100)   NULL,
    [Message]         NVARCHAR(2000)  NOT NULL,
    [StackTrace]      NVARCHAR(MAX)   NULL,
    [CreatedAt]       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_SystemLogs] PRIMARY KEY CLUSTERED ([LogId])
);
CREATE INDEX [IX_SystemLogs_Level] ON [dbo].[SystemLogs] ([Level]);
CREATE INDEX [IX_SystemLogs_Category] ON [dbo].[SystemLogs] ([Category]);
CREATE INDEX [IX_SystemLogs_CreatedAt] ON [dbo].[SystemLogs] ([CreatedAt]);
GO

-- ==================== 视图 ====================
CREATE VIEW [dbo].[V_VitalSigns] AS
SELECT
    vs.VitalSignId, vs.MessageId, vs.PatientId, vs.VisitId,
    vs.VitalSignType, vs.VitalSignName,
    vs.ValueNumeric, vs.ValueString, vs.Units,
    vs.Systolic, vs.Diastolic, vs.MeanPressure,
    vs.OriginalCode, vs.OriginalText,
    vs.AbnormalFlags, vs.ReferenceRange,
    vs.ObservationDateTime, vs.ReceivedAt, vs.DeviceId,
    p.Name AS PatientName, p.Gender, p.DateOfBirth,
    v.Department, v.Ward, v.Bed
FROM [dbo].[VitalSigns] vs
LEFT JOIN [dbo].[Patients] p ON vs.PatientId = p.PatientId
LEFT JOIN [dbo].[Visits] v ON vs.VisitId = v.VisitId
WHERE vs.VitalSignType IN ('HR','SPO2','RESP','NIBP_SYS','NIBP_DIA','NIBP_MEAN','PULSE','IBP_SYS','IBP_DIA','IBP_MEAN');
GO

-- ==================== 种子数据 42 条 ====================
INSERT INTO [dbo].[IdentifierMappings] ([SourceSystem],[SourceCode],[SourceText],[VitalSignType],[VitalSignName],[LoincCode]) VALUES
('Philips_MDIL','0002-4182','HR','HR','心率','8867-4'),
('Philips_MDIL','0002-4bb8','SpO2','SPO2','血氧饱和度','2708-6'),
('Philips_MDIL','0002-5000','Resp','RESP','呼吸频率','9279-1'),
('Philips_MDIL','0002-4a05','NBPs','NIBP_SYS','无创收缩压','8480-6'),
('Philips_MDIL','0002-4a06','NBPd','NIBP_DIA','无创舒张压','8462-4'),
('Philips_MDIL','0002-4a07','NBPm','NIBP_MEAN','无创平均压','8414-5'),
('Philips_MDIL','0401-0068','YSI','TEMP','体温','8310-5'),
('Philips_MDIL','0002-5164','O2','O2','氧气浓度',''),
('Philips_MDIL','0002-4822','Pulse (SpO2)','PULSE','脉率',''),
('Philips_MDIL','0002-4bb0','Perf','PERF','灌注指数',''),
('Philips_MDIL','0002-f0e5','Pulse (NBP)','PULSE','脉率',''),
('Philips_MDIL','0002-f828','SpRR','RESP','呼吸频率',''),
('Philips_MDIL','0002-3f20','QT','QT','QT间期',''),
('Philips_MDIL','0002-3f24','QTc','QTC','QTc间期',''),
('Philips_MDIL','0002-4261','PVC','PVC','室性早搏',''),
('Philips_MDIL','0002-580b','ICPm','ICP','颅内压',''),
('Philips_MDIL','0002-5804','CPP','CPP','脑灌注压',''),
('Philips_MDIL','0002-4a1f','PAPm','PAP_MEAN','肺动脉平均压',''),
('Philips_MDIL','0002-4a1d','PAPs','PAP_SYS','肺动脉收缩压',''),
('Philips_MDIL','0002-4a1e','PAPd','PAP_DIA','肺动脉舒张压',''),
('Philips_MDIL','0002-4a17','ABPm','ABP_MEAN','动脉平均压',''),
('Philips_MDIL','0002-4a15','ABPs','ABP_SYS','动脉收缩压',''),
('Philips_MDIL','0002-4a16','ABPd','ABP_DIA','动脉舒张压',''),
('Philips_MDIL','0002-f0c7','T1','TEMP','体温',''),
('Philips_MDIL','0002-500a','RR','RESP','呼吸频率',''),
('Philips_MDIL','0002-0301','ST-I','ST_I','ST-I',''),
('Philips_MDIL','0002-0302','ST-II','ST_II','ST-II',''),
('Philips_MDIL','0002-033d','ST-III','ST_III','ST-III',''),
('Philips_MDIL','0002-033e','ST-aVR','ST_AVR','ST-aVR',''),
('Philips_MDIL','0002-033f','ST-aVL','ST_AVL','ST-aVL',''),
('Philips_MDIL','0002-0340','ST-aVF','ST_AVF','ST-aVF',''),
('Philips_MDC','147842','MDC_ECG_HEART_RATE','HR','心率','8867-4'),
('Philips_MDC','150456','MDC_PULS_OXIM_SAT_O2','SPO2','血氧饱和度','2708-6'),
('Philips_MDC','151562','MDC_RESP_RATE','RESP','呼吸频率','9279-1'),
('Philips_MDC','150033','MDC_PRESS_BLD_ART_SYS','IBP_SYS','有创收缩压',''),
('Philips_MDC','150034','MDC_PRESS_BLD_ART_DIA','IBP_DIA','有创舒张压',''),
('Philips_MDC','150035','MDC_PRESS_BLD_ART_MEAN','IBP_MEAN','有创平均压',''),
('Philips_MDC','150344','MDC_TEMP','TEMP','体温','8310-5'),
('Philips_MDC','149522','MDC_BLD_PULS_RATE_INV','PULSE','脉率',''),
('Philips_MDC','149530','MDC_PULS_OXIM_PULS_RATE','PULSE','脉率',''),
('Philips_MDC','151712','MDC_CONC_AWAY_CO2_EXP','ETCO2','呼末二氧化碳',''),
('Philips_MDC','150087','MDC_PRESS_BLD_VEN_CENT_MEAN','CVP','中心静脉压','');
GO

PRINT N'=== HL7Gateway 建表完成 ===';
GO
