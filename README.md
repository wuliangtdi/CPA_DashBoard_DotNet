# CPA_DashBoard_DotNet

`CPA_Dashboard` 的 ASP.NET Core 版本控制面板，用于管理 `CLIProxyAPI` 服务、查看日志、维护账户、刷新配额以及生成接入说明。

## 功能

- 服务控制：启动、停止、重启 `CLIProxyAPI`
- 日志查看：读取、尾随、清理日志
- 账户管理：列出账户、删除账户、发起 OAuth 登录
- 配额处理：刷新单个或全部账户配额，校验账号状态
- 使用说明：生成 API 调用示例和接入信息

## 技术栈

- .NET 10
- ASP.NET Core Web API
- `YamlDotNet`

## 目录结构

```text
CPA_DashBoard_DotNet/
├─ CPA_DashBoard_DotNet.sln
├─ src/
│  └─ CPA_DashBoard.Web/
│     ├─ Controllers/
│     ├─ Services/
│     ├─ Models/
│     ├─ Data/
│     └─ wwwroot/
└─ README.md
```

## 运行前准备

1. 安装 .NET 10 SDK
2. 准备 `CLIProxyAPI` 的 `config.yaml` 和认证目录
3. 建议将本仓库与 `CPA_DashBoard_Python` 放在同一级目录

当前项目会优先复用 Python 版本的首页模板：

- 运行时优先读取 `../CPA_DashBoard_Python/templates/index.html`
- 构建时也会把该模板链接到输出目录

如果你只单独使用本仓库，请确保：

- 同级目录存在 `CPA_DashBoard_Python/templates/index.html`
- 或自行调整 `src/CPA_DashBoard.Web/CPA_DashBoard.Web.csproj`
- 或补充你自己的 `src/CPA_DashBoard.Web/wwwroot/index.html`

## 快速开始

### 1. 还原和构建

```bash
dotnet restore
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build CPA_DashBoard_DotNet.sln
```

### 2. 启动开发环境

```bash
dotnet run --project src/CPA_DashBoard.Web
```

开发环境默认会读取 `src/CPA_DashBoard.Web/Properties/launchSettings.json`，常见地址为：

- `http://localhost:5081`
- `https://localhost:7166`

最终监听地址以终端输出为准。

## 配置说明

程序会优先通过环境变量，其次通过 `config.yaml` 解析运行环境。

### 常用环境变量

| 变量 | 说明 |
|------|------|
| `CPA_CONFIG_PATH` | 指定 `config.yaml` 的绝对路径 |
| `CPA_AUTH_DIR` | 认证文件目录，覆盖 `config.yaml` 中的 `auth-dir` |
| `CPA_SERVICE_DIR` | `CLIProxyAPI` 服务目录 |
| `CPA_BINARY_NAME` | 可执行文件名，默认 `CLIProxyAPI` |
| `CPA_LOG_FILE` | 日志文件路径 |
| `CPA_MANAGEMENT_URL` | Management API 地址 |
| `CPA_MANAGEMENT_KEY` | Management API 密钥 |
| `CPA_PROXY_URL` | 对外请求时使用的代理地址 |
| `CPA_QUOTA_REFRESH_CONCURRENCY` | 批量刷新配额并发数，范围 `1-32` |
| `CPA_ANTIGRAVITY_CLIENT_ID` | Antigravity OAuth Client ID |
| `CPA_ANTIGRAVITY_CLIENT_SECRET` | Antigravity OAuth Client Secret |

### `config.yaml` 中使用的键

```yaml
host: 127.0.0.1
port: 8317
auth-dir: ~/.cli-proxy-api
quota-refresh-concurrency: 4
proxy-url: http://127.0.0.1:7890
```

## OAuth 客户端配置

为避免把密钥提交到仓库，`Gemini CLI` 与 `iFlow` 的 OAuth 参数改为从配置或环境变量读取。

### 方式一：环境变量

```bash
export GEMINI_CLI_CLIENT_ID="your-client-id"
export GEMINI_CLI_CLIENT_SECRET="your-client-secret"
export IFLOW_CLIENT_ID="your-iflow-client-id"
export IFLOW_CLIENT_SECRET="your-iflow-client-secret"
```

Windows PowerShell:

```powershell
$env:GEMINI_CLI_CLIENT_ID = "your-client-id"
$env:GEMINI_CLI_CLIENT_SECRET = "your-client-secret"
$env:IFLOW_CLIENT_ID = "your-iflow-client-id"
$env:IFLOW_CLIENT_SECRET = "your-iflow-client-secret"
```

### 方式二：`appsettings.Development.json`

```json
{
  "OAuthClients": {
    "GeminiCli": {
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret"
    },
    "Iflow": {
      "ClientId": "your-iflow-client-id",
      "ClientSecret": "your-iflow-client-secret"
    }
  }
}
```

如果未配置这几个值，相关 Token 刷新校验会返回 `missing_client_config`。

## 运行模式

- 本地模式：直接读取认证目录中的账号文件
- API 模式：配置 `CPA_MANAGEMENT_KEY` 后通过 Management API 获取数据

## 开发说明

### 常用命令

```bash
dotnet build CPA_DashBoard_DotNet.sln
dotnet run --project src/CPA_DashBoard.Web
dotnet publish src/CPA_DashBoard.Web -c Release -o publish
```

## GitHub Actions 构建

仓库内置了跨平台构建工作流：`.github/workflows/build-binaries.yml:1`

### 产物说明

每次手动触发或推送 `v*` 标签后，Actions 会生成以下 ZIP 包：

- `CPA_DashBoard_DotNet-win-x64-framework-dependent.zip`
- `CPA_DashBoard_DotNet-win-x64-self-contained.zip`
- `CPA_DashBoard_DotNet-win-arm64-framework-dependent.zip`
- `CPA_DashBoard_DotNet-win-arm64-self-contained.zip`
- `CPA_DashBoard_DotNet-linux-x64-framework-dependent.zip`
- `CPA_DashBoard_DotNet-linux-x64-self-contained.zip`
- `CPA_DashBoard_DotNet-linux-arm64-framework-dependent.zip`
- `CPA_DashBoard_DotNet-linux-arm64-self-contained.zip`
- `CPA_DashBoard_DotNet-osx-x64-framework-dependent.zip`
- `CPA_DashBoard_DotNet-osx-x64-self-contained.zip`
- `CPA_DashBoard_DotNet-osx-arm64-framework-dependent.zip`
- `CPA_DashBoard_DotNet-osx-arm64-self-contained.zip`

### 触发方式

#### 方式一：手动触发

1. 打开 GitHub 仓库的 `Actions`
2. 选择 `Build Binaries`
3. 点击 `Run workflow`
4. 如果只想生成 Actions 产物，`release_tag` 留空即可
5. 如果想同时发布到 GitHub Release，在 `release_tag` 中填写例如 `v1.0.0`

#### 方式二：推送版本标签

```bash
git tag v1.0.0
git push origin v1.0.0
```

推送 `v*` 标签后，工作流会自动：

- 构建 6 个平台的 12 个 ZIP 包
- 上传到当前 Action 运行记录的 `Artifacts`
- 创建或更新对应的 GitHub Release，并把 ZIP 包作为 Release 资产上传

说明：

- `framework-dependent` 版本需要目标机器预装 .NET 10 Runtime
- `self-contained` 版本自带运行时，体积更大，但可直接运行
- 当前工作流会额外检出 `dongshuyan/CPA-Dashboard`，用于复用 Python 版本的前端模板文件
- 手动触发时，只有填写了 `release_tag` 才会发布到 GitHub Release

### 已验证

```bash
dotnet build CPA_DashBoard_DotNet.sln
```

## 注意事项

- 仓库默认忽略了 `.idea/`、`bin/`、`obj/` 和 `*.log`
- 当前前端模板仍依赖 Python 版本的 `index.html`
- 若 GitHub Push Protection 拦截推送，请先检查是否误提交了 OAuth Client Secret 或其他令牌
