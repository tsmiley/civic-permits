namespace CivicPermits.Web.A11yTests;

/// <summary>
/// Every test class in this project shares one <see cref="SiteFixture"/>: one site process and one browser for the whole run, not one per class.
/// Tests in a collection run one after another, so only one browser is ever using the profile folder (see <see cref="SiteFixture"/>).
/// </summary>
[CollectionDefinition(Name)]
public sealed class SiteBrowserGroup : ICollectionFixture<SiteFixture>
{
    /// <summary>The name the test classes give in <c>[Collection]</c>.</summary>
    public const string Name = "Site";
}
