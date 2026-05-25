using System;
using Xunit;
using PeekabooWin.Core.Memory;

namespace PeekabooWin.Core.Tests;

public class AppProfileTests
{
    [Fact]
    public void FromWindowSignature_BrowserWindow_SetsCorrectAnchors()
    {
        var sig = new WindowSignature
        {
            ProcessName = "msedge.exe",
            WindowTitle = "Doubao AI",
            WindowType = "browser",
            InputMode = "web_textbox",
            RiskDomain = "external_ai_chat"
        };

        var profile = AppProfile.FromWindowSignature(sig);

        Assert.Equal("msedge", profile.AppId);
        Assert.Equal("Doubao AI", profile.AppName);
        Assert.Equal("browser", profile.WindowType);
        Assert.Equal("web_textbox", profile.InputMode);
        Assert.Contains("input_box", profile.KnownAnchors);
        Assert.Contains("send_btn", profile.KnownAnchors);
    }

    [Fact]
    public void FromWindowSignature_EditField_EditRegion()
    {
        var sig = new WindowSignature
        {
            ProcessName = "notepad.exe",
            WindowTitle = "notepad",
            WindowType = "editor",
            InputMode = "edit_field",
            RiskDomain = "neutral"
        };

        var profile = AppProfile.FromWindowSignature(sig);

        Assert.Equal("notepad", profile.AppId);
        Assert.Contains("edit_region", profile.KnownAnchors);
    }

    [Fact]
    public void FromWindowSignature_Dialog_HasOkCancel()
    {
        var sig = new WindowSignature
        {
            ProcessName = "explorer.exe",
            WindowTitle = "confirm",
            WindowType = "dialog",
            InputMode = "dialog_input",
            RiskDomain = "neutral"
        };

        var profile = AppProfile.FromWindowSignature(sig);

        Assert.Contains("ok_btn", profile.KnownAnchors);
        Assert.Contains("cancel_btn", profile.KnownAnchors);
    }

    [Fact]
    public void IsCompatibleWith_WildcardScope_AlwaysTrue()
    {
        var profile = new AppProfile { AppId = "anyapp" };
        var scope = new SkillScope { SupportedApps = new List<string> { "*" } };

        Assert.True(profile.IsCompatibleWith(scope));
    }

    [Fact]
    public void IsCompatibleWith_AppInList_True()
    {
        var profile = new AppProfile { AppId = "notepad" };
        var scope = new SkillScope { SupportedApps = new List<string> { "notepad", "wordpad" } };

        Assert.True(profile.IsCompatibleWith(scope));
    }

    [Fact]
    public void IsCompatibleWith_AppNotInList_False()
    {
        var profile = new AppProfile { AppId = "chrome" };
        var scope = new SkillScope { SupportedApps = new List<string> { "notepad", "wordpad" } };

        Assert.False(profile.IsCompatibleWith(scope));
    }

    [Fact]
    public void IsCompatibleWith_NullScope_True()
    {
        var profile = new AppProfile { AppId = "anyapp" };
        Assert.True(profile.IsCompatibleWith(null));
    }

    [Fact]
    public void Touch_IncrementsVisitCount()
    {
        var profile = new AppProfile { AppId = "test", VisitCount = 5 };
        profile.Touch();

        Assert.Equal(6, profile.VisitCount);
        Assert.True(profile.LastSeen > profile.FirstSeen);
    }

    [Fact]
    public void WindowSignature_SimilarityTo_SameType_HighScore()
    {
        var a = new WindowSignature { WindowType = "browser", InputMode = "web_textbox", RiskDomain = "neutral" };
        var b = new WindowSignature { WindowType = "browser", InputMode = "web_textbox", RiskDomain = "neutral" };

        Assert.True(a.SimilarityTo(b) > 0.05);
    }

    [Fact]
    public void WindowSignature_SimilarityTo_DifferentType_LowScore()
    {
        var a = new WindowSignature { WindowType = "browser", InputMode = "web_textbox", RiskDomain = "neutral" };
        var b = new WindowSignature { WindowType = "editor", InputMode = "edit_field", RiskDomain = "neutral" };

        Assert.True(a.SimilarityTo(b) < 0.5);
    }

    [Fact]
    public void WindowSignature_BelongsToSameFamily_ChromeAndEdge()
    {
        var a = new WindowSignature { ProcessName = "chrome.exe", WindowType = "browser" };
        var b = new WindowSignature { ProcessName = "msedge.exe", WindowType = "browser" };

        Assert.True(a.SimilarityTo(b) >= 0);
    }
}