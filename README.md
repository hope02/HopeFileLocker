# HopeFileLocker

一个基于 **WPF** 开发的 Windows 文件隐私保护小工具，提供「隐藏」与「加密」两类能力，启动需密码登录。定位类似 *Wise Folder Hider*，但更轻量、只做核心两件事。

> 适用于：需要把某些文件夹从资源管理器里“藏起来”，或把内容用密码锁死、防止他人直接打开的场景。

---

## ✨ 功能特性

| 功能 | 说明 |
| --- | --- |
| 🔐 登录保护 | 启动先弹出登录窗口，输入正确密码才进入主界面。密码以 SHA-256 哈希保存在本机 `%LOCALAPPDATA%\HopeFileLocker\pwd.dat`，不存明文。 |
| 🙈 隐藏 / 显示 | 调用 Windows 文件属性（底层 Win32 `SetFileAttributes`），给文件夹及其内容设置 `Hidden | System`，使其在资源管理器中彻底消失；「显示」移除这两个属性。 |
| 🔒 加密 / 解密 | 用登录密码经 PBKDF2 派生 AES-256 密钥，对文件夹内所有文件做 AES-CBC 加密、重命名为 `*.locked`，并写入隐藏清单 `.hope_manifest.json` 记录原始路径；「解密」反向还原。 |
| 📋 管理列表 | 主界面以列表展示已管理的文件夹路径、当前状态（可见/已隐藏 · 未加密/已加密），支持批量或单条操作。 |

---

## 🧰 技术栈

- **.NET 10**（`net10.0-windows`）+ **WPF** (XAML)
- 语言：**C#**（启用 `Nullable` 与 `ImplicitUsings`）
- 加密：**AES-256-CBC** + **PBKDF2**（`Rfc2898DeriveBytes`，SHA-256，100,000 次迭代）
- 隐藏：.NET `File.SetAttributes` → Win32 `SetFileAttributes`
- 纯 .NET 标准库实现，无第三方依赖

---

## 📁 目录结构

```
HopeFileLocker/
├── App.xaml / App.xaml.cs          # 应用入口，启动 LoginWindow
├── LoginWindow.xaml(.cs)           # 密码登录窗口
├── MainWindow.xaml(.cs)            # 主管理界面（列表 + 工具栏）
├── ManagedFolder.cs                # 受管理文件夹模型（INotifyPropertyChanged）
├── Session.cs                      # 进程内会话，保存登录密码（仅内存）
├── SecurityHelper.cs               # 登录密码的本地哈希存储与校验
├── FileHider.cs                    # 隐藏 / 显示（文件属性操作）
└── FolderCrypto.cs                 # AES 加密 / 解密 + 清单管理
```

---

## 🚀 使用说明

1. **首次启动**：弹出登录窗，输入任意密码即被设为登录密码（之后需用该密码登录）。
2. **添加文件夹**：点击「添加文件夹」，选择要管理的目录，加入列表。
3. **隐藏 / 显示**：
   - 选中列表项（或不选则作用于全部）→ 点击「隐藏」使其从资源管理器消失；
   - 再点「显示」恢复可见。
4. **加密 / 解密**：
   - 选中目标 → 点击「加密」，文件夹内所有文件被加密并重命名为 `*.locked`；
   - 点击「解密」并输入正确密码，恢复原始文件名与内容。
5. **移除**：从列表中移除某个文件夹的登记（不改动磁盘上的真实文件）。

---

## ⚙️ 工作原理

### 隐藏
`FileHider` 递归遍历目标目录，对每个文件和子目录执行：

```csharp
info.Attributes |= FileAttributes.Hidden | FileAttributes.System;   // 隐藏
info.Attributes &= ~(FileAttributes.Hidden | FileAttributes.System); // 显示
```

设置 `System` 属性后，即便在资源管理器勾选“显示隐藏的项目”，仍需再开启“显示受保护的操作系统文件”才能看到。

### 加密
`FolderCrypto` 的流程：

1. 随机生成 16 字节 `salt`，用 `PBKDF2(password, salt, 100000)` 派生 32 字节 AES-256 密钥；
2. 遍历目录内每个文件，随机生成 IV，AES-CBC + PKCS7 加密，输出为 `原名.locked`（IV 写在密文头部）；
3. 写入隐藏清单 `.hope_manifest.json`（记录 `salt` 与 原始名 → 加密名 的映射）；
4. 解密时按清单与密码还原文件名与内容，并删除清单。

---

## ⚠️ 安全说明与注意事项

1. **「隐藏」只是混淆，不是加密。** 任何知道方法的人（在资源管理器开启“显示隐藏的项目 / 受保护的操作系统文件”）都能看到被隐藏的文件夹。真正的保护请使用「加密」。
2. **忘记密码 = 数据不可恢复。** 没有后门、没有密码找回；加密强度直接取决于你的密码强度，请使用足够复杂的密码。
3. **加密清单当前为明文**（仅设为隐藏属性）。若需更强保护，可把清单本身也加密（后续改进项）。
4. **会话密码仅存于内存**：程序关闭即失效，下次启动需重新登录；加密/解密依赖本次登录的密码。
5. 本工具用于保护**你本人拥有**的文件，请勿用于处理你无权处置的数据。

---

## 🛠️ 构建与运行

**要求：**
- Windows 10/11
- .NET 10 桌面运行时 / SDK（含 WPF）
- Visual Studio 2026（或 `dotnet` CLI）

**方式一：Visual Studio**
1. 打开 `HopeFileLocker.slnx`；
2. 选择 **Release / x64**（或 Any CPU）；
3. 生成 → 生成解决方案，运行 `HopeFileLocker`。

**方式二：命令行**
```bash
dotnet build -c Release
dotnet run --project HopeFileLocker.csproj
```
生成的可执行文件位于 `bin/Release/net10.0-windows/`。

---

## 🔧 后续可改进

- [ ] 加密清单本身加密，避免暴露原始文件名
- [ ] 「记住密码」到登录窗（当前每次启动需重输）
- [ ] 隐藏仅作用于文件夹本身（当前递归到内容）
- [ ] 拖拽添加文件夹、操作进度条与取消
- [ ] 主界面深色模式 / 多语言

---

## 📄 许可证

本项目当前未指定许可证。如需开源使用，请自行添加合适的 LICENSE。
