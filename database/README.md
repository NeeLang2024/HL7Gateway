# HL7Gateway 数据库脚本

SQL Server 建库与增量迁移脚本。SQLite 开发环境无需执行本目录脚本，配置 `ConnectionStrings:Sqlite` 后由 WebApi 自动建表即可。

## 脚本说明

| 文件 | 用途 |
|------|------|
| `HL7Gateway_Init.sql` | **全新环境**：创建数据库 `HL7Gateway`、11 张 HL7 核心表、视图 `V_VitalSigns`、飞利浦 MDIL 体征映射种子数据 |
| `HL7Gateway_CreateTables.sql` | **库已存在**：仅建 11 张核心表（不含建库语句，适合 DBA 已建好空库的场景） |
| `HL7Gateway_Migrate_20250614.sql` | **升级/补全**：`PatientLocation` 列、Auto ADT 相关表（床位、绑定、事件、消息、扫码规则、功能开关） |

## 推荐方式（自动）

1. 在 SQL Server 上创建空库（或让登录账号有建库权限）。
2. 在 `appsettings.json` 中配置 `DatabaseProvider` 与 `ConnectionStrings:SqlServer`。
3. **首次启动 HL7Gateway.WebApi** — 由 `DbInitializer` 自动建表、补列、建索引，并插入默认管理员与体征映射种子数据。

> **注意**：仅 WebApi 进程执行 DDL；Service 进程不会改表结构，避免双进程锁库。

## 手工方式（SSMS）

适合需要 DBA 预审表结构、或 WebApi 暂时无法启动的场景：

```text
1. 执行 HL7Gateway_Init.sql          （全新安装）
   或 HL7Gateway_CreateTables.sql     （空库已存在）
2. 执行 HL7Gateway_Migrate_20250614.sql （补 Auto ADT 与后续字段）
3. 仍建议启动一次 WebApi，以创建 Users / WsiSubscriptions 等扩展表及性能索引
```

## 表清单（共 19 张）

**HL7 核心**

- `Patients`、`Visits`、`HL7Messages`、`ParsedSegments`、`Observations`、`VitalSigns`
- `IdentifierMappings`、`ADTQueue`、`ADTLog`、`DeviceConnections`、`SystemLogs`

**扩展（WebApi 启动时或迁移脚本创建）**

- `Users` — Web 登录
- `WsiSubscriptions` — WSI 订阅（可选）
- `AutoAdtBeds`、`AutoAdtBindings`、`AutoAdtEvents`、`AutoAdtMessages` — 扫码入院
- `AutoAdtScanRules` — 扫码规则
- `AutoAdtSettings` — 功能开关

实体定义见 `src/HL7Gateway.Core/Entities/`，索引见 `src/HL7Gateway.Core/DbContexts/Hl7GatewayDbContext.cs`。
