using System.Net;
using CivicPermits.Infrastructure.Persistence.Seeding;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Api.IntegrationTests;

/// <summary>The audit record of permit-type administration: one entry per action, none for a refusal, read only.</summary>
public class AuditTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PermitTypeRequest Values(string name = "Garden room permit", long fee = 12_500, int rate = 150) =>
        new("Building", name, "A detached room in a back garden.", fee, rate, true, 365, 14);

    private static async Task<IReadOnlyList<AuditEntryResponse>> EntriesAsync(HttpClient administrator)
    {
        HttpResponseMessage response = await administrator.GetAsync("/api/v1/audit?pageSize=100", Token);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PagedResponse<AuditEntryResponse>>(Token))!.Items;
    }

    [Fact]
    public async Task Creating_changing_closing_and_opening_a_permit_type_each_write_exactly_one_entry_naming_the_administrator_and_the_values()
    {
        using SeededApi api = new();
        using HttpClient administrator = await api.SignInAsync(SeededApi.Administrator, Token);
        Assert.Empty(await EntriesAsync(administrator));

        // Create.
        HttpResponseMessage created = await administrator.PostAsJsonAsync("/api/v1/permit-types", Values(), Token);
        PermitTypeResponse type = (await created.Content.ReadFromJsonAsync<PermitTypeResponse>(Token))!;
        IReadOnlyList<AuditEntryResponse> afterCreate = await EntriesAsync(administrator);

        AuditEntryResponse create = Assert.Single(afterCreate);
        Assert.Equal("permit_type.created", create.Action);
        Assert.Equal("PermitType", create.SubjectType);
        Assert.Equal(type.Id, create.SubjectId);
        Assert.Equal(SampleIdentities.Administrator, create.ActorId);
        Assert.False(string.IsNullOrWhiteSpace(create.ActorName));
        Assert.Contains("Garden room permit", create.Summary, StringComparison.Ordinal);
        Assert.Contains("$125.00", create.Summary, StringComparison.Ordinal);
        Assert.Contains("1.5%", create.Summary, StringComparison.Ordinal);
        Assert.InRange(create.OccurredOn, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));

        // Change the fee: one more entry, with the old and the new value.
        await administrator.PutAsJsonAsync($"/api/v1/permit-types/{type.Id}", Values(fee: 15_000), Token);
        IReadOnlyList<AuditEntryResponse> afterChange = await EntriesAsync(administrator);

        Assert.Equal(2, afterChange.Count);
        AuditEntryResponse change = afterChange[0];
        Assert.Equal("permit_type.revised", change.Action);
        Assert.Contains("base fee from $125.00 to $150.00", change.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("value share", change.Summary, StringComparison.Ordinal);

        // Close, then open.
        await administrator.PostAsync($"/api/v1/permit-types/{type.Id}/close", content: null, Token);
        await administrator.PostAsync($"/api/v1/permit-types/{type.Id}/open", content: null, Token);
        IReadOnlyList<AuditEntryResponse> all = await EntriesAsync(administrator);

        Assert.Equal(["permit_type.opened", "permit_type.closed", "permit_type.revised", "permit_type.created"], all.Select(entry => entry.Action));
        Assert.All(all, entry => Assert.Equal(type.Id, entry.SubjectId));
        Assert.Contains("it was closed", all[0].Summary, StringComparison.Ordinal);
        Assert.Contains("it was open", all[1].Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_or_failed_action_writes_no_entry()
    {
        using SeededApi api = new();
        using HttpClient administrator = await api.SignInAsync(SeededApi.Administrator, Token);
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient anonymous = api.Anonymous();
        Guid existing = (await administrator.GetFromJsonAsync<List<PermitTypeResponse>>("/api/v1/permit-types?includeInactive=true", Token))![0].Id;
        string existingName = (await administrator.GetFromJsonAsync<PermitTypeResponse>($"/api/v1/permit-types/{existing}", Token))!.Name;

        // Not allowed to: a clerk, an applicant, nobody signed in.
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsJsonAsync("/api/v1/permit-types", Values(), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.PutAsJsonAsync($"/api/v1/permit-types/{existing}", Values(), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsync($"/api/v1/permit-types/{existing}/close", content: null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/v1/permit-types/{existing}/open", content: null, Token)).StatusCode);

        // Allowed to, but the request is wrong: a bad value, a name already taken, a type that does not exist.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await administrator.PostAsJsonAsync("/api/v1/permit-types", Values(rate: 5_000), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PostAsJsonAsync("/api/v1/permit-types", Values(name: existingName), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PutAsJsonAsync($"/api/v1/permit-types/{Guid.NewGuid()}", Values(), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.PostAsync($"/api/v1/permit-types/{Guid.NewGuid()}/close", content: null, Token)).StatusCode);

        Assert.Empty(await EntriesAsync(administrator));
    }

    [Fact]
    public async Task Only_administrators_can_read_the_record_and_its_paging_is_checked()
    {
        using SeededApi api = new();
        using HttpClient administrator = await api.SignInAsync(SeededApi.Administrator, Token);
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient inspector = await api.SignInAsync(SeededApi.InspectorOne, Token);
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient anonymous = api.Anonymous();

        Assert.Equal(HttpStatusCode.OK, (await administrator.GetAsync("/api/v1/audit", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync("/api/v1/audit", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await inspector.GetAsync("/api/v1/audit", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.GetAsync("/api/v1/audit", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/audit", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await administrator.GetAsync("/api/v1/audit?page=0", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await administrator.GetAsync("/api/v1/audit?pageSize=101", Token)).StatusCode);
    }

    [Fact]
    public async Task The_record_is_paged_newest_first()
    {
        using SeededApi api = new();
        using HttpClient administrator = await api.SignInAsync(SeededApi.Administrator, Token);

        for (int i = 1; i <= 3; i++)
        {
            await administrator.PostAsJsonAsync("/api/v1/permit-types", Values(name: $"Pageable permit {i}"), Token);
        }

        PagedResponse<AuditEntryResponse> first = (await administrator.GetFromJsonAsync<PagedResponse<AuditEntryResponse>>("/api/v1/audit?page=1&pageSize=2", Token))!;
        PagedResponse<AuditEntryResponse> second = (await administrator.GetFromJsonAsync<PagedResponse<AuditEntryResponse>>("/api/v1/audit?page=2&pageSize=2", Token))!;

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Contains("Pageable permit 3", first.Items[0].Summary, StringComparison.Ordinal);
        Assert.Contains("Pageable permit 1", second.Items[0].Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_in_the_API_can_add_to_change_or_remove_an_entry()
    {
        using SeededApi api = new();

        string[] methods =
        [.. api.Factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText!.Contains("/audit", StringComparison.OrdinalIgnoreCase))
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
            .Distinct(StringComparer.Ordinal)];

        Assert.Equal(["GET"], methods);
    }
}
