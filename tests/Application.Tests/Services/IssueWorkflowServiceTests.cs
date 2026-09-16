using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Services;

public class IssueWorkflowServiceTests
{
    private readonly IssueWorkflowService _workflowService;

    public IssueWorkflowServiceTests()
    {
        // IssueWorkflowService CanTransition and GetAllowedTransitions don't use injected dependencies
        _workflowService = new IssueWorkflowService(null!, null!, null!, null!);
    }

    [Fact]
    public void CanTransition_NewToAcknowledged_AllowedForRegularUser()
    {
        var result = _workflowService.CanTransition(IssueStatus.New, IssueStatus.Acknowledged, isAdmin: false);
        result.Should().BeTrue();
    }

    [Fact]
    public void CanTransition_NewToPlanned_RequiresAdmin()
    {
        var regularUserResult = _workflowService.CanTransition(IssueStatus.New, IssueStatus.Planned, isAdmin: false);
        var adminUserResult = _workflowService.CanTransition(IssueStatus.New, IssueStatus.Planned, isAdmin: true);

        regularUserResult.Should().BeFalse();
        adminUserResult.Should().BeTrue();
    }

    [Fact]
    public void CanTransition_InvalidTransition_ReturnsFalse()
    {
        var result = _workflowService.CanTransition(IssueStatus.New, IssueStatus.Verified, isAdmin: true);
        result.Should().BeFalse();
    }

    [Fact]
    public void GetAllowedTransitions_ForNewStatus_ReturnsExpectedTargetStatuses()
    {
        var regularTransitions = _workflowService.GetAllowedTransitions(IssueStatus.New, isAdmin: false);
        var adminTransitions = _workflowService.GetAllowedTransitions(IssueStatus.New, isAdmin: true);

        regularTransitions.Should().Contain(new[] { IssueStatus.Acknowledged, IssueStatus.Triaged, IssueStatus.Closed });
        regularTransitions.Should().NotContain(IssueStatus.Planned);

        adminTransitions.Should().Contain(new[] { IssueStatus.Acknowledged, IssueStatus.Triaged, IssueStatus.Planned, IssueStatus.Closed });
    }
}
