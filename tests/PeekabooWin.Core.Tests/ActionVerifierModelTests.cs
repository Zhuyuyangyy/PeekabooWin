using Xunit;
using PeekabooWin.Core.Verification;

namespace PeekabooWin.Core.Tests;

public class ActionVerifierModelTests
{
    [Fact]
    public void VerificationResult_DefaultValues_AreExpected()
    {
        var result = new VerificationResult();

        Assert.Equal(VerificationStatus.Passed, result.Status);
        Assert.Equal(string.Empty, result.Action);
        Assert.Equal(string.Empty, result.Reason);
        Assert.Equal(0.0, result.Confidence);
        Assert.Null(result.BeforeScreenshot);
        Assert.Null(result.AfterScreenshot);
        Assert.Null(result.BeforeText);
        Assert.Null(result.AfterText);
        Assert.Equal(0, result.BeforeElementCount);
        Assert.Equal(0, result.AfterElementCount);
    }

    [Fact]
    public void VerificationStatus_EnumHas_Passed_Failed_Inconclusive()
    {
        var values = Enum.GetValues<VerificationStatus>();

        Assert.Contains(VerificationStatus.Passed, values);
        Assert.Contains(VerificationStatus.Failed, values);
        Assert.Contains(VerificationStatus.Inconclusive, values);
    }

    [Fact]
    public void VerificationRequest_CanBeConstructedWithActionAndArgs()
    {
        var args = new Dictionary<string, string> { ["text"] = "hello", ["window"] = "Notepad" };
        var request = new VerificationRequest
        {
            Action = "type",
            Args = args
        };

        Assert.Equal("type", request.Action);
        Assert.NotNull(request.Args);
        Assert.Equal("hello", request.Args["text"]);
        Assert.Equal("Notepad", request.Args["window"]);
    }

    [Fact]
    public void VerificationResult_Confidence_DefaultsToZero()
    {
        var result = new VerificationResult();

        Assert.Equal(0.0, result.Confidence);
    }

    [Fact]
    public void VerificationResult_CanDistinguishBetween_PassedAndFailed()
    {
        var passed = new VerificationResult { Status = VerificationStatus.Passed, Confidence = 0.9 };
        var failed = new VerificationResult { Status = VerificationStatus.Failed, Confidence = 0.2 };

        Assert.NotEqual(passed.Status, failed.Status);
        Assert.Equal(VerificationStatus.Passed, passed.Status);
        Assert.Equal(VerificationStatus.Failed, failed.Status);
    }
}
