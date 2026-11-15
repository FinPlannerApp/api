using Application.Common.Helpers;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Common;

public class ReportFingerprintTests
{
    private static string Context(string page, string error, string path = "/api/Transactions/search", int status = 500) =>
        $$"""{"page":"{{page}}","consoleErrors":["{{error}}"],"failedApiCalls":[{"method":"POST","path":"{{path}}","status":{{status}}}]}""";

    [Fact]
    public void TheSameProblemOnDifferentRecordsHasTheSameFingerprint()
    {
        var a = ReportFingerprint.From(Context("/app/accounts/12/transactions", "10:01:22 TypeError: Cannot read properties of undefined (reading 5)"));
        var b = ReportFingerprint.From(Context("/app/accounts/99/transactions", "23:59:59 TypeError: Cannot read properties of undefined (reading 7)"));

        a.Should().NotBeNull();
        a.Should().Be(b);
    }

    [Fact]
    public void ADifferentProblemHasADifferentFingerprint()
    {
        var a = ReportFingerprint.From(Context("/app/accounts/12/transactions", "TypeError: x is undefined"));
        var otherPage = ReportFingerprint.From(Context("/app/budgets", "TypeError: x is undefined"));
        var otherError = ReportFingerprint.From(Context("/app/accounts/12/transactions", "ReferenceError: y is not defined"));
        var otherStatus = ReportFingerprint.From(Context("/app/accounts/12/transactions", "TypeError: x is undefined", status: 404));

        new[] { a, otherPage, otherError, otherStatus }.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"page":"/app/dashboard","consoleErrors":[],"failedApiCalls":[]}""")] // nothing to match on
    public void NothingToMatchOnGivesNoFingerprint(string? json)
    {
        ReportFingerprint.From(json).Should().BeNull();
    }
}
