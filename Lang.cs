using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HopeFileLocker;

/// <summary>
/// 语言枚举：中文 / 英文。
/// </summary>
public enum LangCode { Zh, En }

/// <summary>
/// 轻量多语言支持：内置中 / 英双语词典，所有 UI 文本经 T(key) 获取。
/// 默认跟随系统区域，用户可在登录窗口手动切换并持久化到 lang.cfg。
/// 切换语言时触发 Changed 事件，已订阅的窗口会自动刷新文本。
/// </summary>
public static class Lang
{
    public static LangCode Current { get; private set; } = LangCode.Zh;
    public static event Action? Changed;

    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HopeFileLocker");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "lang.cfg");

    private static readonly Dictionary<string, string> Zh = new()
    {
        ["appTitle"] = "HopeFileLocker · 文件隐私保护工具",
        ["brandSub"] = "文件隐私保护工具",

        // 登录窗口
        ["loginPwd"] = "密码",
        ["loginBtn"] = "登 录",
        ["loginForgot"] = "忘记密码？",
        ["loginHintFirst"] = "首次使用：输入任意密码即设为登录密码，并会生成救援码。",
        ["loginHintNormal"] = "请输入登录密码以解锁主界面。",
        ["loginErrNoPwd"] = "本机尚未设置密码，请直接输入密码登录。",
        ["loginErrEmpty"] = "请输入密码。",
        ["loginErrWrong"] = "密码错误，请重试。",
        ["rescueTipFirst"] = "请保存你的救援码：忘记密码时凭它找回，且已加密的文件不会丢失。",
        ["rescueTipRecovered"] = "密码已找回！请保存这串新的救援码（旧的可能会失效）：",

        // 主界面
        ["mainAdd"] = "添加文件夹",
        ["mainToggleHide"] = "隐藏 / 显示",
        ["mainToggleEnc"] = "加密 / 解密",
        ["mainRemove"] = "移除",
        ["colSelect"] = "选择",
        ["colPath"] = "文件夹路径",
        ["colStatus"] = "状态",
        ["colActions"] = "操作",
        ["actOpen"] = "打开",
        ["actRemove"] = "移除",
        ["statusCount"] = "共 {0} 个文件夹",
        ["busyProcessing"] = "处理中…",
        ["busyEncrypt"] = "正在加密…",
        ["busyDecrypt"] = "正在解密…",
        ["busyRestore"] = "正在恢复并移除…",
        ["dlgRestoreTitle"] = "移除并恢复",
        ["dlgRestoreMsg"] = "将从列表移除该文件夹，并先恢复其文件（解密已加密内容、取消隐藏）。\n{0}\n\n继续？",
        ["msgInList"] = "该文件夹已在列表中。",
        ["msgNoSession"] = "会话密码缺失，请重新登录。",
        ["msgSelectTarget"] = "请先勾选要操作的文件夹（每行左侧的勾选框，可多选），或按住 Ctrl/Shift 在列表中多选，再点击工具栏按钮。",
        ["msgCryptoFail"] = "加密/解密失败：{0}",
        ["msgRestoreFail"] = "恢复失败：{0}\n（该文件夹保留在列表中，未移除）",
        ["restoreNoPwd"] = "会话密码缺失，无法解密：{0}（请重新登录后再移除）",
        ["dlgSelectFolder"] = "选择要管理的文件夹",
        ["removeFailTitle"] = "移除失败",

        // 动态按钮与状态
        ["actShow"] = "显示",
        ["actHide"] = "隐藏",
        ["actDecrypt"] = "解密",
        ["actEncrypt"] = "加密",
        ["stHidden"] = "已隐藏",
        ["stVisible"] = "可见",
        ["stEncrypted"] = "已加密",
        ["stNotEnc"] = "未加密",

        // 找回密码窗口
        ["rescueTitle"] = "找回密码",
        ["rescueHeader"] = "找回登录密码",
        ["rescueMethod1"] = "方式一：输入救援码",
        ["rescueOk"] = "用救援码找回",
        ["rescueErrWrong"] = "救援码不正确，请检查后重试。",
        ["rescueCancel"] = "取消",

        // 救援码展示窗口
        ["codeTitle"] = "救援码",
        ["codeHeader"] = "你的救援码",
        ["codeSaveHint"] = "请抄写或截图保存。关闭后将不再显示；遗忘登录密码时凭它找回，且已加密的文件不会丢失。",
        ["codeSaved"] = "我已保存",

        // 语言
        ["langLabel"] = "语言",
        ["langZh"] = "中文",
        ["langEn"] = "English"
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["appTitle"] = "HopeFileLocker · File Privacy Tool",
        ["brandSub"] = "File Privacy Tool",

        ["loginPwd"] = "Password",
        ["loginBtn"] = "Sign In",
        ["loginForgot"] = "Forgot password?",
        ["loginHintFirst"] = "First use: enter any password to set it as your login password; a rescue code will be generated.",
        ["loginHintNormal"] = "Enter your login password to unlock.",
        ["loginErrNoPwd"] = "No password set on this device. Enter a password to sign in.",
        ["loginErrEmpty"] = "Please enter your password.",
        ["loginErrWrong"] = "Wrong password, please try again.",
        ["rescueTipFirst"] = "Save your rescue code: use it to recover if you forget the password, and your encrypted files will not be lost.",
        ["rescueTipRecovered"] = "Password recovered! Save this new rescue code (the old one may become invalid):",

        ["mainAdd"] = "Add Folder",
        ["mainToggleHide"] = "Hide / Show",
        ["mainToggleEnc"] = "Encrypt / Decrypt",
        ["mainRemove"] = "Remove",
        ["colSelect"] = "Select",
        ["colPath"] = "Folder Path",
        ["colStatus"] = "Status",
        ["colActions"] = "Actions",
        ["actOpen"] = "Open",
        ["actRemove"] = "Remove",
        ["statusCount"] = "{0} folders",
        ["busyProcessing"] = "Processing…",
        ["busyEncrypt"] = "Encrypting…",
        ["busyDecrypt"] = "Decrypting…",
        ["busyRestore"] = "Restoring and removing…",
        ["dlgRestoreTitle"] = "Remove and Restore",
        ["dlgRestoreMsg"] = "This folder will be removed from the list, and its files restored first (decrypt encrypted content, unhide).\n{0}\n\nContinue?",
        ["msgInList"] = "This folder is already in the list.",
        ["msgNoSession"] = "Session password missing, please sign in again.",
        ["msgSelectTarget"] = "Please check the folders to operate (the checkbox on the left of each row, supports multi-select), or hold Ctrl/Shift to multi-select in the list, then click a toolbar button.",
        ["msgCryptoFail"] = "Encrypt/Decrypt failed: {0}",
        ["msgRestoreFail"] = "Restore failed: {0}\n(The folder remains in the list and was not removed)",
        ["restoreNoPwd"] = "Session password missing, cannot decrypt: {0} (please sign in again before removing)",
        ["dlgSelectFolder"] = "Select folder to manage",
        ["removeFailTitle"] = "Remove failed",

        ["actShow"] = "Show",
        ["actHide"] = "Hide",
        ["actDecrypt"] = "Decrypt",
        ["actEncrypt"] = "Encrypt",
        ["stHidden"] = "Hidden",
        ["stVisible"] = "Visible",
        ["stEncrypted"] = "Encrypted",
        ["stNotEnc"] = "Not encrypted",

        ["rescueTitle"] = "Recover Password",
        ["rescueHeader"] = "Recover login password",
        ["rescueMethod1"] = "Method 1: Enter rescue code",
        ["rescueOk"] = "Recover with code",
        ["rescueErrWrong"] = "Incorrect rescue code, please check and retry.",
        ["rescueCancel"] = "Cancel",

        ["codeTitle"] = "Rescue Code",
        ["codeHeader"] = "Your rescue code",
        ["codeSaveHint"] = "Please write it down or take a screenshot. It will not be shown again after closing; use it to recover if you forget the password, and your encrypted files will not be lost.",
        ["codeSaved"] = "I've saved it",

        ["langLabel"] = "Language",
        ["langZh"] = "中文",
        ["langEn"] = "English"
    };

    static Lang()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var v = File.ReadAllText(ConfigPath).Trim().ToLowerInvariant();
                Current = v == "en" ? LangCode.En : LangCode.Zh;
                return;
            }
        }
        catch { /* 忽略，走系统区域判断 */ }

        var name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Current = name == "zh" ? LangCode.Zh : LangCode.En;
    }

    /// <summary>获取当前语言的文本；若 key 缺失则返回 key 本身（便于发现遗漏）。</summary>
    public static string T(string key)
    {
        var dict = Current == LangCode.Zh ? Zh : En;
        return dict.TryGetValue(key, out var v) ? v : key;
    }

    /// <summary>切换语言并持久化；若与当前不同则触发 Changed 事件。</summary>
    public static void Set(LangCode code)
    {
        if (Current == code) return;
        Current = code;
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, code == LangCode.En ? "en" : "zh");
        }
        catch { /* 持久化失败不阻塞切换 */ }
        Changed?.Invoke();
    }
}
