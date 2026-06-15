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

> **免责声明**：本软件仅供集成开发与研究使用，不构成医疗器械。用于临床环境前请自行完成验证、合规与风险评估。

---

## 环境要求

- **Windows Server / Windows 10+**（Service、WebApi、桥接件）
- **.NET 9 SDK**（编译中间件）
- **Node.js 20+**（编译前端）
- **SQL Server** 或 **SQLite**（开发）
- **Visual Studio 2022** + .NET Framework 4.x 桌面开发 workload（**仅编译桥接件**）
- **Philips PIC iX 安装介质中的 HIF/PPIS DLL**（**不可随本仓库分发**，见下文）

---

## 快速开始

### 1. 克隆与配置

```bash
git clone https://github.com/YOUR_ORG/HL7Gateway.git
cd HL7Gateway
```

复制配置模板并修改数据库连接、JWT 密钥：

```text
src/HL7Gateway.WebApi/appsettings.example.json  →  appsettings.json
src/HL7Gateway.Service/appsettings.example.json   →  appsettings.json
```

**切勿将含真实密码的 `appsettings.json` 提交到 Git。**

### 2. 准备 Philips DLL（桥接件）

从已授权 PIC iX 服务器复制 Philips SDK DLL 到仓库根目录：

```text
dll_NEW/Philips.PlatformServices.dll   （及 csproj 引用的依赖项）
```

这些文件受 Philips 许可约束，**本仓库不包含、也不授权再分发**。

### 3. 编译打包（macOS / Linux / Windows）

```bash
# 中间件 + 前端（跨平台）
cd frontend && npm install && cd ..
./scripts/pack.sh
```

产物：`publish/HL7Gateway.zip`（含 Service、WebApi、bridge 脚本；桥接 exe 需在 Windows 上编译）

### 4. 编译桥接件（仅 Windows）

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
scripts/pack.sh         # 打包脚本
publish/*.bat           # 安装/卸载脚本（无编译产物）
LICENSE
README.md
.gitignore
```

**不要上传**：`publish/Service/`、`publish/WebApi/`、`bin/`、`obj/`、`node_modules/`、`*.zip`、`dll/`、`docs/`、含密码的配置文件。

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

欢迎 Issue 与 Pull Request。提交前请确保未包含 Philips 专有 DLL、数据库密码或内部文档。

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

> **Disclaimer**: For integration and research only. Not a medical device. Validate for clinical use yourself.

---

## Requirements

- Windows Server / Windows 10+
- .NET 9 SDK
- Node.js 20+
- SQL Server or SQLite
- Visual Studio 2022 with .NET Framework desktop workload (bridge only)
- Philips HIF/PPIS DLLs from a **licensed PIC iX installation** (not included in this repo)

---

## Quick start

### 1. Clone & configure

```bash
git clone https://github.com/YOUR_ORG/HL7Gateway.git
cd HL7Gateway
```

Copy example configs and set connection strings / JWT secret:

```text
src/HL7Gateway.WebApi/appsettings.example.json  →  appsettings.json
src/HL7Gateway.Service/appsettings.example.json   →  appsettings.json
```

Never commit real credentials.

### 2. Philips DLLs (bridge)

Copy SDK DLLs from your PIC iX server into `dll_NEW/` per `tools/PhilipsHifBridge/PhilipsHifBridge.csproj`.  
**Redistribution is not permitted.**

### 3. Build middleware (cross-platform)

```bash
cd frontend && npm install && cd ..
./scripts/pack.sh
```

Output: `publish/HL7Gateway.zip`

### 4. Build bridge (Windows only)

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

Include: `src/`, `frontend/` (source only), `tools/`, `scripts/`, root `publish/*.bat`, `LICENSE`, `README.md`, `.gitignore`.

Exclude: build outputs, `node_modules/`, Philips DLLs, zip releases, internal docs, secrets.

**No GitHub Releases**: source only — build locally with `./scripts/pack.sh`; do not attach zip/exe to Releases.

---

## License

[MIT](LICENSE)
