using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;
using SvmApi.Api.Auth;
using SvmApi.Application.Auth;

namespace SvmApi.Tests.Auth;

/// <summary>Permission policies (US-002, C1): deny by default, perm:&lt;code&gt; policies, RequirePermission attribute.</summary>
public class PermissionAuthorizationTests
{
    [Fact]
    public async Task Handler_succeeds_for_authenticated_user_with_the_exact_permission()
    {
        var context = await Run(new StubUser(true, ["asset.read"]), "asset.read");

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_succeeds_for_family_wildcard()
    {
        var context = await Run(new StubUser(true, ["admin.*"]), "admin.users.read");

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData("asset.write")]
    [InlineData("audit.read")]
    public async Task Handler_does_not_succeed_without_the_permission(string required)
    {
        var context = await Run(new StubUser(true, ["asset.read"]), required);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_does_not_succeed_for_bare_wildcard_grant()
    {
        var context = await Run(new StubUser(true, ["*"]), "asset.read");

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_denies_unauthenticated_user_even_if_permissions_are_present()
    {
        var context = await Run(new StubUser(false, ["asset.read"]), "asset.read");

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Policy_provider_builds_permission_policy_requiring_authentication()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        var policy = await provider.GetPolicyAsync(PermissionPolicies.For("asset.read"));

        Assert.NotNull(policy);
        Assert.Contains(policy!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        var requirement = Assert.Single(policy.Requirements.OfType<PermissionRequirement>());
        Assert.Equal("asset.read", requirement.Permission);
    }

    [Fact]
    public async Task Policy_provider_returns_null_for_unknown_non_permission_policy()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        Assert.Null(await provider.GetPolicyAsync("not-a-permission-policy"));
    }

    [Fact]
    public void RequirePermission_attribute_maps_to_permission_policy_name()
    {
        var attribute = new RequirePermissionAttribute("audit.read");

        Assert.Equal("perm:audit.read", attribute.Policy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequirePermission_attribute_rejects_blank_permission(string permission)
        => Assert.Throws<ArgumentException>(() => new RequirePermissionAttribute(permission));

    private static async Task<AuthorizationHandlerContext> Run(ICurrentUser user, string permission)
    {
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(new ClaimsIdentity()), null);

        await new PermissionAuthorizationHandler(user).HandleAsync(context);
        return context;
    }

    private sealed class StubUser(bool authenticated, string[] permissions) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public Guid? UserId => authenticated ? Guid.NewGuid() : null;
        public Guid? SessionId => null;
        public string? Username => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => permissions;
        public IReadOnlyCollection<AuthScope> Scopes => [];
        public bool HasPermission(string permission) => PermissionMatcher.HasPermission(permissions, permission);
    }
}
