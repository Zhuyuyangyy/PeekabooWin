using System.Net.Http;
using Xunit;
using PeekabooWin.Core.Agent;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Tests;

public class TaskParserTests
{
    private readonly TaskParser _parser = new(new HttpClient());

    [Fact]
    public void ParseTask_ClickWithCoordinates_ReturnsClickAction()
    {
        var steps = _parser.ParseTask("click 100 200");

        Assert.Single(steps);
        Assert.Equal("click", steps[0].Action);
        Assert.Equal("100", steps[0].Args["x"]);
        Assert.Equal("200", steps[0].Args["y"]);
    }

    [Fact]
    public void ParseTask_TypeText_ReturnsTypeAction()
    {
        var steps = _parser.ParseTask("type hello");

        Assert.Single(steps);
        Assert.Equal("type", steps[0].Action);
        Assert.Equal("hello", steps[0].Args["text"]);
    }

    [Fact]
    public void ParseTask_PressKey_ReturnsPressAction()
    {
        var steps = _parser.ParseTask("press enter");

        Assert.Single(steps);
        Assert.Equal("press", steps[0].Action);
        Assert.Equal("enter", steps[0].Args["key"]);
    }

    [Fact]
    public void ParseTask_PressHotkey_ReturnsHotkeyAction()
    {
        var steps = _parser.ParseTask("press ctrl+a");

        Assert.Single(steps);
        Assert.Equal("hotkey", steps[0].Action);
        Assert.Equal("ctrl+a", steps[0].Args["keys"]);
    }

    [Fact]
    public void ParseTask_OpenApp_ReturnsFocusWindowAction()
    {
        var steps = _parser.ParseTask("open notepad");

        Assert.Single(steps);
        Assert.Equal("focus-window", steps[0].Action);
        Assert.Equal("notepad", steps[0].Args["title"]);
    }

    [Fact]
    public void ParseTask_Screenshot_ReturnsScreenshotAction()
    {
        var steps = _parser.ParseTask("screenshot");

        Assert.Single(steps);
        Assert.Equal("screenshot", steps[0].Action);
    }

    [Fact]
    public void ParseTask_UnknownTask_NoApiKey_ReturnsErrorAction()
    {
        var originalKey = Environment.GetEnvironmentVariable("MINIMAX_API_KEY");
        Environment.SetEnvironmentVariable("MINIMAX_API_KEY", null);

        try
        {
            var steps = _parser.ParseTask("do something completely unknown xyz");

            Assert.Single(steps);
            Assert.Equal("error", steps[0].Action);
            Assert.NotEmpty(_parser.LastFallbackReason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINIMAX_API_KEY", originalKey);
        }
    }

    [Fact]
    public void GetLastParseMetadata_AfterRuleBasedParse_ReturnsCorrectMode()
    {
        _parser.ParseTask("click 100 200");

        var meta = _parser.GetLastParseMetadata();

        Assert.Equal("rule_based", meta.ParserMode);
        Assert.True(meta.LlmEnabled);
    }
}
