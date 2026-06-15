# HL7Gateway

**中文** | [English](#english)

开源 HL7 集成网关：接收 MLLP 消息、Web 管理界面、Philips PIC iX Auto ADT 桥接、扫码入院。

---

## 简介

HL7Gateway 是一套面向医院的 HL7 中间件，包含：

| 组件 | 说明 |
|------|------|
| **HL7Gateway.Service** | Windows 服务：MLLP 2575 监听、HL7 解析/存储、ADT 发送队列 |
| **HL7Gateway.WebApi** | Windows 服务：Vue 管理界面 + REST API + SignalR |
| **PhilipsHifBridge** | 独立 Windows 服务：WCF/PPIS 桥接，供 PIC iX 订阅与接收 ADT |
| **Auto ADT** | 腕带 + 床位码扫码入院、床位看板、绑定管理 |

> **免责声明**
>
> 本项目 **仅供 HL7 / 设备集成的开发、联调与功能验证**，用于帮助理解消息流、接口对接与调试流程。
>
> **不得用于临床诊疗、患者监护、医嘱执行或任何直接影响患者安全与医疗决策的场景。** 本软件 **不是医疗器械**，未经过临床验证、注册或认证。
>
> 任何在生产或临床环境中部署、使用本软件的行为，均由使用者自行承担全部验证、合规与法律责任。作者不对因使用本软件造成的任何损害承担责任。

---

## 环境要求

- **Windows Server / Windows 10+**（Service、WebApi、桥接件）
- **.NET 9 SDK**（编译中间件）
- **Node.js 20+**（编译前端）
- **SQL Server** 或 **SQLite**（开发）
- **Visual Studio 2022** + .NET Framework 4.x 桌面开发 workload（**仅编译桥接件**）

> **第三方专有组件**：若需与 Philips PIC iX 等设备集成并引用其专有库，须**自行向 Philips 官方取得合法授权**。**本仓库不包含、也不提供任何 Philips 或其他第三方的专有文件**；**严禁分发、上传或再传播未经授权的相关文件**。使用者须自行承担合规与许可责任。

---

## 数据库初始化

支持 **SQL Server**（生产）与 **SQLite**（本地开发）。默认数据库名：`HL7Gateway`。

### 方式 A：自动建表（推荐）

1. 在 SQL Server 上准备空库（或赋予登录账号建库权限）。
2. 复制 `appsettings.example.json` → `appsettings.json`，设置 `DatabaseProvider` 与连接串：

```json
"DatabaseProvider": "SqlServer",
"ConnectionStrings": {
  "SqlServer": "Server=YOUR_HOST;Database=HL7Gateway;User Id=...;Password=...;TrustServerCertificate=True;"
}
```

3. **首次启动 HL7Gateway.WebApi** — 程序内 `DbInitializer` 会自动建表、补列、建索引，并创建默认账号 `admin` / `admin123` 与飞利浦 MDIL 体征映射种子数据。

SQLite 开发：将 `DatabaseProvider` 设为 `Sqlite`，连接串指向 `.db` 文件即可，同样由 WebApi 自动初始化。

> 仅 **WebApi** 执行 DDL；**Service** 进程不改表结构，避免双进程同时迁移锁死 SQL Server。

### 方式 B：手工执行 SQL（SSMS）

`database/` 目录提供脚本（详细说明见本地 `docs/database.md`，不上传 GitHub）：

| 脚本 | 用途 |
|------|------|
| `HL7Gateway_Init.sql` | 建库 + 11 张核心表 + 视图 + 种子数据 |
| `HL7Gateway_CreateTables.sql` | 仅建 11 张核心表（库已存在时） |
| `HL7Gateway_Migrate_20250614.sql` | Auto ADT 表、`PatientLocation` 列等增量迁移 |

手工建表后，仍建议 **启动一次 WebApi**，以补全 `Users`、`WsiSubscriptions`、性能索引等运行时扩展。

### 表结构概览（19 张）

| 分组 | 表名 |
|------|------|
| HL7 核心 | `Patients`, `Visits`, `HL7Messages`, `ParsedSegments`, `Observations`, `VitalSigns`, `IdentifierMappings`, `ADTQueue`, `ADTLog`, `DeviceConnections`, `SystemLogs` |
| 扩展 | `Users`, `WsiSubscriptions`, `AutoAdtBeds`, `AutoAdtBindings`, `AutoAdtEvents`, `AutoAdtMessages`, `AutoAdtScanRules`, `AutoAdtSettings` |

源码参考：`src/HL7Gateway.Core/DbContexts/Hl7GatewayDbContext.cs`、`src/HL7Gateway.Core/Data/DbInitializer.cs`。

---

## 快速开始

### 1. 克隆与配置

```bash
git clone https://github.com/NeeLang2024/HL7Gateway.git
cd HL7Gateway
```

复制配置模板并修改数据库连接、JWT 密钥：

```text
src/HL7Gateway.WebApi/appsettings.example.json  →  appsettings.json
src/HL7Gateway.Service/appsettings.example.json   →  appsettings.json
```

**切勿将含真实密码的 `appsettings.json` 提交到 Git。**

### 2. 第三方专有组件（桥接件）

桥接功能可能依赖 Philips 等厂商的专有库。**本仓库仅提供集成示例源码，不包含任何第三方专有文件。**

- 如需引用 Philips 相关组件，须**事先取得 Philips 官方书面或合同授权**；
- **严禁**将未经授权的专有库、安装介质副本或衍生包随本仓库、安装包或公开渠道**分发、上传或再传播**；
- 编译、部署桥接件所需专有组件，须由使用者在**合法授权范围内**自行准备；
- 因未获授权或违规使用第三方组件而产生的一切法律与合规风险，由使用者自行承担。

### 3. 编译打包（macOS / Linux / Windows）

```bash
# 中间件 + 前端（跨平台）
cd frontend && npm install && cd ..
./scripts/pack.sh
```

产物：`publish/HL7Gateway.zip`（含 Service、WebApi、bridge 脚本；桥接 exe 需在 Windows 上编译）

### 4. 编译桥接件（仅 Windows，可选）

```bat
cd tools\PhilipsHifBridge
build.bat
```

将生成的 `bin\Release\PhilipsHifBridge.exe` 复制到 `publish\bridge\tools\PhilipsHifBridge\bin\Release\`。

### 5. 安装（Windows 目标机）

解压 `publish/` 到固定目录，例如 `C:\HL7Gateway`：

```bat
:: 中间件（Service + WebApi）
install.bat

:: 桥接件（单独，需管理员）
cd bridge
install-bridge-service.bat
```

- Web 界面：<http://localhost:5002>
- 默认账号：`admin` / `admin123`（**首次登录后请修改**）
- MLLP 端口：**2575**
- 桥接 HTTP：**5080**，WCF：**9912**

### 6. 卸载

```bat
uninstall.bat
cd bridge && uninstall-bridge-service.bat
```

---

## 主要功能

- HL7 MLLP 接收（ORU/ADT 等）与解析入库
- Vue 3 仪表盘、消息列表、生命体征、设备连接监控
- ADT 队列与 Philips 桥接状态
- **Auto ADT**：扫码入院 A01 / 转床 A02 / 出院 A03 / 更新 A08
- 床位映射、床位看板、扫码规则（可选）
- **功能开关**（默认关闭增强项）：`Auto ADT → 功能开关`

---

## 仓库结构（开源上传范围）

```text
src/                    # .NET 源码（Core / Service / WebApi）
frontend/               # Vue 3 前端源码
tools/PhilipsHifBridge/ # 桥接件 C# 源码
database/               # SQL Server 建库与迁移脚本
scripts/pack.sh         # 打包脚本
publish/*.bat           # 安装/卸载脚本（无编译产物）
LICENSE
README.md
.gitignore
```

**不要上传**：`publish/Service/`、`publish/WebApi/`、`bin/`、`obj/`、`node_modules/`、`*.zip`、未经授权的第三方专有库、`docs/`、含密码的配置文件。

**不提供 GitHub Release**：仓库仅含源码，使用者自行 `./scripts/pack.sh` 编译；不在 Releases 页面上传 zip 或 exe。

---

## 端口一览

| 端口 | 用途 |
|------|------|
| 2575 | HL7 MLLP 入站 |
| 5002 | Web 管理界面 |
| 5080 | 桥接 HTTP（/status、/adt） |
| 9912 | PIC iX WCF 订阅 |

---

## 贡献

欢迎 Issue 与 Pull Request。提交前请确保未包含未经授权的第三方专有文件、数据库密码或内部文档。

---

---

<a id="english"></a>

# English

Open-source HL7 integration gateway: MLLP ingestion, Vue admin UI, Philips PIC iX Auto ADT bridge, and barcode bedside admission.

---

## Overview

| Component | Role |
|-----------|------|
| **HL7Gateway.Service** | Windows service: MLLP listener, HL7 parse/store, ADT queue |
| **HL7Gateway.WebApi** | Windows service: Vue UI + REST API + SignalR |
| **PhilipsHifBridge** | Separate Windows service: WCF/PPIS bridge for PIC iX |
| **Auto ADT** | Wristband + bed barcode admission, bed board, bindings |

> **Disclaimer**
>
> This project is **for development, integration testing, and functional debugging only** — to explore HL7 messaging, interface wiring, and troubleshooting workflows.
>
> **It must not be used for clinical care, patient monitoring, order execution, or any scenario that directly affects patient safety or medical decisions.** This software is **not a medical device** and has **not** been clinically validated, registered, or certified.
>
> Anyone who deploys or uses this software in production or clinical settings does so at their own risk and is solely responsible for validation, compliance, and legal obligations. The authors accept no liability for harm arising from use of this software.

---

## Requirements

- Windows Server / Windows 10+
- .NET 9 SDK
- Node.js 20+
- SQL Server or SQLite
- Visual Studio 2022 with .NET Framework desktop workload (bridge only)

> **Third-party proprietary components**: Integration with Philips PIC iX or similar systems may require vendor libraries that you must **obtain under official authorization from Philips**. **This repository does not include or supply any Philips or other third-party proprietary files.** **Unauthorized distribution, upload, or redistribution of such materials is strictly prohibited.** You are solely responsible for licensing and compliance.

---

## Database setup

Supports **SQL Server** (production) and **SQLite** (local dev). Default database name: `HL7Gateway`.

### Option A: Auto schema (recommended)

1. Create an empty SQL Server database (or grant the login permission to create one).
2. Copy `appsettings.example.json` → `appsettings.json` and set `DatabaseProvider` + connection string.
3. **Start HL7Gateway.WebApi once** — `DbInitializer` creates tables, applies incremental migrations, seeds the default `admin` / `admin123` user and Philips MDIL identifier mappings.

For SQLite, set `DatabaseProvider` to `Sqlite` and point the connection string at a `.db` file.

> Only **WebApi** runs DDL. **Service** does not alter schema (avoids dual-process migration locks).

### Option B: Manual SQL (SSMS)

SQL scripts live in `database/` (see local `docs/database.md` for details; not in the public repo):

| Script | Purpose |
|--------|---------|
| `HL7Gateway_Init.sql` | Create DB + 11 core tables + view + seed data |
| `HL7Gateway_CreateTables.sql` | Core tables only (DB already exists) |
| `HL7Gateway_Migrate_20250614.sql` | Auto ADT tables, `PatientLocation` column, etc. |

After manual scripts, still **start WebApi once** to create `Users`, `WsiSubscriptions`, performance indexes, and other runtime extensions.

### Tables (19)

Core HL7: `Patients`, `Visits`, `HL7Messages`, `ParsedSegments`, `Observations`, `VitalSigns`, `IdentifierMappings`, `ADTQueue`, `ADTLog`, `DeviceConnections`, `SystemLogs`.

Extensions: `Users`, `WsiSubscriptions`, `AutoAdtBeds`, `AutoAdtBindings`, `AutoAdtEvents`, `AutoAdtMessages`, `AutoAdtScanRules`, `AutoAdtSettings`.

See `src/HL7Gateway.Core/DbContexts/Hl7GatewayDbContext.cs` and `src/HL7Gateway.Core/Data/DbInitializer.cs`.

---

## Quick start

### 1. Clone & configure

```bash
git clone https://github.com/NeeLang2024/HL7Gateway.git
cd HL7Gateway
```

Copy example configs and set connection strings / JWT secret:

```text
src/HL7Gateway.WebApi/appsettings.example.json  →  appsettings.json
src/HL7Gateway.Service/appsettings.example.json   →  appsettings.json
```

Never commit real credentials.

### 2. Third-party components (bridge)

The bridge may depend on proprietary libraries from Philips or other vendors. **This repository provides integration sample source code only and includes no third-party proprietary files.**

- To reference Philips components, you must **obtain official authorization from Philips** in advance;
- **Do not** distribute, upload, or redistribute unauthorized proprietary libraries, installation media copies, or derived packages with this repo, installers, or public channels;
- Any proprietary files required to build or deploy the bridge must be supplied by you **within the scope of your valid license**;
- You bear all legal and compliance risks arising from unauthorized or non-compliant use of third-party components.

### 3. Build middleware (cross-platform)

```bash
cd frontend && npm install && cd ..
./scripts/pack.sh
```

Output: `publish/HL7Gateway.zip`

### 4. Build bridge (Windows only, optional)

```bat
cd tools\PhilipsHifBridge
build.bat
```

Copy `PhilipsHifBridge.exe` into the publish tree under `publish\bridge\tools\PhilipsHifBridge\bin\Release\`.

### 5. Install on Windows

```bat
install.bat
cd bridge && install-bridge-service.bat
```

- UI: <http://localhost:5002> — default `admin` / `admin123` (change after first login)
- MLLP: **2575** | Bridge HTTP: **5080** | WCF: **9912**

### 6. Uninstall

```bat
uninstall.bat
cd bridge && uninstall-bridge-service.bat
```

---

## What to publish on GitHub

Include: `src/`, `frontend/` (source only), `tools/`, `database/`, `scripts/`, root `publish/*.bat`, `LICENSE`, `README.md`, `.gitignore`.

Exclude: build outputs, `node_modules/`, unauthorized third-party proprietary files, zip releases, internal docs, secrets.

**No GitHub Releases**: source only — build locally with `./scripts/pack.sh`; do not attach zip/exe to Releases.

---

## License

[MIT](LICENSE)
