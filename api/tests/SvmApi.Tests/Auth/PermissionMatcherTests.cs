using SvmApi.Application.Auth;

namespace SvmApi.Tests.Auth;

/// <summary>PermissionMatcher (US-002, C1): exact, family wildcard, ไม่มี global wildcard, deny by default.</summary>
public class PermissionMatcherTests
{
    [Theory]
    [InlineData("finding.assign", "finding.assign")]
    [InlineData("Finding.Assign", "finding.assign")]
    [InlineData("finding.assign", "FINDING.ASSIGN")]
    public void Exact_match_succeeds_case_insensitively(string grant, string required)
        => Assert.True(PermissionMatcher.Matches(grant, required));

    [Theory]
    [InlineData("finding.assign", "finding.read")]
    [InlineData("finding.assign", "finding.assign.all")]
    [InlineData("finding.assign.all", "finding.assign")]
    public void Different_permission_is_denied(string grant, string required)
        => Assert.False(PermissionMatcher.Matches(grant, required));

    [Theory]
    [InlineData("admin.*", "admin.users.read")]
    [InlineData("admin.*", "admin.roles")]
    [InlineData("Admin.*", "admin.users.read")]
    [InlineData("admin.*", "ADMIN.USERS.READ")]
    [InlineData("admin.users.*", "admin.users.read")]
    public void Family_wildcard_matches_inside_the_family(string grant, string required)
        => Assert.True(PermissionMatcher.Matches(grant, required));

    [Theory]
    [InlineData("admin.*", "administrator.x")]
    [InlineData("admin.*", "admin")]
    [InlineData("admin.*", "admin.")]
    [InlineData("admin.*", "finding.read")]
    [InlineData("admin.users.*", "admin.roles.read")]
    [InlineData("finding.*", "findings.read")]
    public void Family_wildcard_does_not_cross_the_family_boundary(string grant, string required)
        => Assert.False(PermissionMatcher.Matches(grant, required));

    [Theory]
    [InlineData("*", "admin.users.read")]
    [InlineData("*.*", "admin.users.read")]
    [InlineData(".*", "admin.users.read")]
    [InlineData("**", "admin.users.read")]
    public void Bare_wildcard_is_not_a_global_grant(string grant, string required)
        => Assert.False(PermissionMatcher.Matches(grant, required));

    [Theory]
    [InlineData("", "admin.users.read")]
    [InlineData("   ", "admin.users.read")]
    [InlineData("admin.*", "")]
    [InlineData("admin.*", "   ")]
    [InlineData("", "")]
    public void Empty_values_are_denied(string grant, string required)
        => Assert.False(PermissionMatcher.Matches(grant, required));

    [Fact]
    public void HasPermission_is_true_when_any_grant_matches()
    {
        var grants = new[] { "asset.read", "admin.*" };

        Assert.True(PermissionMatcher.HasPermission(grants, "admin.users.read"));
        Assert.True(PermissionMatcher.HasPermission(grants, "asset.read"));
    }

    [Fact]
    public void HasPermission_is_false_for_empty_or_unrelated_grants()
    {
        Assert.False(PermissionMatcher.HasPermission([], "asset.read"));
        Assert.False(PermissionMatcher.HasPermission(new[] { "asset.read", "admin.*" }, "finding.assign"));
        Assert.False(PermissionMatcher.HasPermission(new[] { "*" }, "asset.read"));
    }

    [Theory]
    [InlineData("perm:asset.read", true, "asset.read")]
    [InlineData("perm:admin.users.read", true, "admin.users.read")]
    [InlineData("perm:", false, "")]
    [InlineData("asset.read", false, "")]
    [InlineData("Perm:asset.read", false, "")]
    public void PermissionPolicies_round_trips_policy_names(string policyName, bool expected, string permission)
    {
        Assert.Equal(expected, PermissionPolicies.TryGetPermission(policyName, out var actual));
        Assert.Equal(permission, actual);
        if (expected)
        {
            Assert.Equal(policyName, PermissionPolicies.For(actual));
        }
    }
}
