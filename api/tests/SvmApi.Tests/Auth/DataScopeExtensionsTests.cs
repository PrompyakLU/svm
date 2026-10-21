using SvmApi.Application.Auth;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Auth.DataScope;

namespace SvmApi.Tests.Auth;

/// <summary>Central data-scope filter (US-003, FR-63, BR-30): union of grants, no grant = nothing, fail closed.</summary>
public class DataScopeExtensionsTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();

    private static readonly Asset[] Assets =
    [
        NewAsset("A-1", "IT", ownerId: OwnerId),
        NewAsset("A-2", "IT"),
        NewAsset("A-3", "HR"),
        NewAsset("A-4", null),
    ];

    [Fact]
    public void Unauthenticated_user_sees_nothing()
    {
        var result = Assets.AsQueryable().ApplyDataScope(new StubUser(authenticated: false, [new("Organization", "*")]));

        Assert.Empty(result);
    }

    [Fact]
    public void User_without_scopes_sees_nothing()
    {
        var result = Assets.AsQueryable().ApplyDataScope(new StubUser(true, []));

        Assert.Empty(result);
    }

    [Fact]
    public void Organization_all_sees_everything()
    {
        var result = Assets.AsQueryable().ApplyDataScope(new StubUser(true, [new("Organization", "*")]));

        Assert.Equal(4, result.Count());
    }

    [Theory]
    [InlineData("IT")]
    [InlineData("")]
    [InlineData("**")]
    public void Organization_with_value_other_than_star_grants_nothing(string value)
    {
        var result = Assets.AsQueryable().ApplyDataScope(new StubUser(true, [new("Organization", value)]));

        Assert.Empty(result);
    }

    [Fact]
    public void Business_unit_scope_filters_by_unit()
    {
        var result = Assets.AsQueryable()
            .ApplyDataScope(new StubUser(true, [new("BusinessUnit", "IT")]))
            .Select(a => a.AssetCode)
            .OrderBy(c => c)
            .ToList();

        Assert.Equal(["A-1", "A-2"], result);
    }

    [Fact]
    public void Asset_scope_filters_by_asset_code()
    {
        var result = Assets.AsQueryable()
            .ApplyDataScope(new StubUser(true, [new("Asset", "A-3")]))
            .Select(a => a.AssetCode)
            .ToList();

        Assert.Equal(["A-3"], result);
    }

    [Fact]
    public void Responsibility_scope_returns_only_assets_owned_by_the_user()
    {
        var result = Assets.AsQueryable()
            .ApplyDataScope(new StubUser(true, [new("Responsibility", "self")], OwnerId))
            .Select(a => a.AssetCode)
            .ToList();

        Assert.Equal(["A-1"], result);
    }

    [Fact]
    public void Multiple_grants_are_combined_as_union()
    {
        var result = Assets.AsQueryable()
            .ApplyDataScope(new StubUser(true, [new("BusinessUnit", "HR"), new("Asset", "A-1")]))
            .Select(a => a.AssetCode)
            .OrderBy(c => c)
            .ToList();

        Assert.Equal(["A-1", "A-3"], result);
    }

    [Fact]
    public void Unknown_scope_type_grants_nothing()
    {
        var result = Assets.AsQueryable().ApplyDataScope(new StubUser(true, [new("Galaxy", "*")]));

        Assert.Empty(result);
    }

    [Fact]
    public void Instances_are_filtered_through_their_asset_and_assignment()
    {
        var instances = new[]
        {
            NewInstance(Assets[0]),                          // IT, owned by OwnerId
            NewInstance(Assets[2]),                          // HR
            NewInstance(Assets[2], assignedTo: OtherId),     // HR but assigned to OtherId
        };

        var byUnit = instances.AsQueryable()
            .ApplyDataScope(new StubUser(true, [new("BusinessUnit", "HR")]))
            .Count();
        var byResponsibility = instances.AsQueryable()
            .ApplyDataScope(new StubUser(true, [new("Responsibility", "self")], OtherId))
            .Count();
        var none = instances.AsQueryable().ApplyDataScope(new StubUser(true, [])).Count();

        Assert.Equal(2, byUnit);
        Assert.Equal(1, byResponsibility);
        Assert.Equal(0, none);
    }

    private static Asset NewAsset(string code, string? businessUnit, Guid? ownerId = null)
    {
        var asset = new Asset { Id = Guid.NewGuid(), AssetCode = code, Name = code, BusinessUnit = businessUnit };
        if (ownerId is not null)
        {
            asset.Ownerships.Add(new AssetOwnership { Id = Guid.NewGuid(), AssetId = asset.Id, Asset = asset, OwnerUserId = ownerId });
        }

        return asset;
    }

    private static VulnerabilityInstance NewInstance(Asset asset, Guid? assignedTo = null)
        => new() { Id = Guid.NewGuid(), AssetId = asset.Id, Asset = asset, Title = "t", AssignedToUserId = assignedTo };

    private sealed class StubUser(bool authenticated, AuthScope[] scopes, Guid? userId = null) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public Guid? UserId => userId;
        public Guid? SessionId => null;
        public string? Username => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
        public IReadOnlyCollection<AuthScope> Scopes => scopes;
        public bool HasPermission(string permission) => false;
    }
}
