# 66门店巡检

门店电脑体检与维护工具，四件套。面向电脑上门服务场景：给店主看结论、给技术服务看数据、给旧机器做清理、给迁移和修复做保障。

Windows 10 / 11 直接运行，用系统自带的 `csc.exe` 编译，**不需要安装 .NET SDK 或运行时**，客户机器零依赖。

## 下载

仓库已公开，下载无需登录 GitHub：

https://github.com/66likedo/66-store-check/releases/download/v1.1.0/66StoreCheck-v1.1.0.zip

解压后得到下面四个程序。最新版本见 [Releases](https://github.com/66likedo/66-store-check/releases)。

## 四个程序

| 程序 | 用途 | 是否改系统 |
|---|---|---|
| `66门店巡检.exe` | 10 项只读体检，生成一页 A4「电脑体检单」（含 PDF），白话写给店主看 | 否 |
| `66专业检测.exe` | 深度采集，生成专业报告，给技术服务做判断 | 否 |
| `66系统清理.exe` | 临时文件、回收站、浏览器缓存清理，备份后一键还原 | 是（可撤销） |
| `66维护工具.exe` | 数据迁移、启动项、目录修复、卸载残留扫描等 11 个维护工具 | 是（需确认） |

设计原则：**能只读就只读**。巡检全程不改系统；清理支持 `--plan` 预演、`--selftest` 沙箱自检、`--restore` 一键还原；维护操作逐项确认并写操作记录。

## 目录结构

```
src/            源码（C#，单文件程序集，无外部依赖）
src/Clean/      66系统清理
src/Pro/        66专业检测
src/Tool/       66维护工具
tools/          开发辅助脚本（A4 版式校验、只读审计等）
dist/           编译产物 + 运行时生成的报告与记录（不入库）
build.bat       编译 66门店巡检
build-pro.bat   编译 66专业检测
build-clean.bat 编译 66系统清理
build-tool.bat  编译 66维护工具
```

`dist/`、报告、清理记录、维护记录都已在 `.gitignore` 中排除——**里面是客户门店名和设备信息，不允许提交到仓库**。

## 编译

双击对应的 `build-*.bat`，或在命令行执行：

```powershell
cd <项目目录>
.\build.bat        # 生成 dist\66门店巡检.exe
.\build-pro.bat    # 生成 dist\66专业检测.exe
.\build-clean.bat  # 生成 dist\66系统清理.exe
.\build-tool.bat   # 生成 dist\66维护工具.exe
```

编译脚本已带 `chcp 65001`，可以直接输出中文文件名。

## 常用自检

```powershell
# 清理引擎沙箱自检（不碰真实文件）
.\dist\66系统清理.exe --selftest

# 只预演清理，不实际删除
.\dist\66系统清理.exe --plan

# 巡检的 A4 打印校验（需要 Edge，输出页数）
.\tools\verify-a4.ps1

# 专业报告的 A4 校验
.\tools\verify-a4.ps1 -Pro
```

## 发布更新流程

前提：已安装 GitHub CLI 并登录（`gh auth login`）。

```powershell
cd <项目目录>

# 1. 提交代码
git add .
git commit -m "改了什么，为什么改"
git push

# 2. 重新编译
.\build.bat; .\build-pro.bat; .\build-clean.bat; .\build-tool.bat

# 3. 打包（必须打包，原因见下方「已知问题」）
$zip = "$env:TEMP\66StoreCheck-v1.1.0.zip"
Compress-Archive -Path .\dist\66*.exe -DestinationPath $zip -Force

# 4. 建 Release 并上传
gh release create v1.1.0 --repo 66likedo/66-store-check `
  --title "v1.1.0 - 66门店巡检四件套" `
  --notes "本次更新内容……"
gh release upload v1.1.0 $zip --repo 66likedo/66-store-check --clobber
```

注意：`gh release upload` 必须在项目目录内执行，或者带上 `--repo` 参数，否则会报 `not a git repository`。

## 已知问题

**GitHub 不支持中文附件名。**
上传中文文件名的 exe 时，接口会把名字截断成 `66.exe`，多个文件互相覆盖。已验证 `gh release upload`、HttpClient、原生 REST 三种方式均复现。所以四个程序统一打包成 `66StoreCheck-版本号.zip` 发布，解压后文件名仍是中文。

## 仓库

https://github.com/66likedo/66-store-check

公开仓库，源码与 Release 附件均可匿名访问，客户直接下载即可。
