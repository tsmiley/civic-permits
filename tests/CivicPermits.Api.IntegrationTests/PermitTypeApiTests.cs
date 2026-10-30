using System.Net;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Api.IntegrationTests;

public class PermitTypeApiTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PermitTypeRequest Valid(string name = "Garden room permit") => new(
        "Building", name, "A small detached room in a back garden used as an office or studio.", 12_000, 100, true, 365, 8);

    [Fact]
    public async Task Anyone_can_list_the_open_permit_types_without_signing_in()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();

        IReadOnlyList<PermitTypeResponse>? types = await client.GetFromJsonAsync<IReadOnlyList<PermitTypeResponse>>("/api/v1/permit-types", Token);

        Assert.NotNull(types);
        Assert.Equal(9, types.Count);
        Assert.All(types, type => Assert.True(type.IsActive));
        Assert.Contains(types, type => type is { Name: "Residential building permit", BaseFeeCents: 15_000, ValuationRateBasisPoints: 125 });
    }

    [Fact]
    public async Task An_administrator_creates_a_type_and_everyone_then_sees_it()
    {
        using SeededApi api = new();
        using HttpClient admin = await api.SignInAsync(SeededApi.Administrator, Token);

        HttpResponseMessage created = await admin.PostAsJsonAsync("/api/v1/permit-types", Valid(), Token);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        PermitTypeResponse type = (await created.Content.ReadFromJsonAsync<PermitTypeResponse>(Token))!;
        Assert.Equal($"/api/v1/permit-types/{type.Id}", created.Headers.Location?.OriginalString);

        using HttpClient anonymous = api.Anonymous();
        PermitTypeResponse? fetched = await anonymous.GetFromJsonAsync<PermitTypeResponse>($"/api/v1/permit-types/{type.Id}", Token);
        Assert.Equal("Garden room permit", fetched?.Name);
        Assert.Equal(10, (await anonymous.GetFromJsonAsync<IReadOnlyList<PermitTypeResponse>>("/api/v1/permit-types", Token))!.Count);
    }

    [Theory]
    [InlineData(SeededApi.Clerk)]
    [InlineData(SeededApi.ApplicantOne)]
    [InlineData(SeededApi.InspectorOne)]
    public async Task Only_an_administrator_may_define_or_change_permit_types(string email)
    {
        using SeededApi api = new();
        using HttpClient client = await api.SignInAsync(email, Token);
        Guid existing = (await client.GetFromJsonAsync<IReadOnlyList<PermitTypeResponse>>("/api/v1/permit-types", Token))![0].Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/permit-types", Valid(), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/v1/permit-types/{existing}", Valid(), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/v1/permit-types/{existing}/close", null, Token)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_define_permit_types()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/permit-types", Valid(), Token)).StatusCode);
    }

    [Fact]
    public async Task Two_types_cannot_share_a_name_even_in_a_different_case()
    {
        using SeededApi api = new();
        using HttpClient admin = await api.SignInAsync(SeededApi.Administrator, Token);

        HttpResponseMessage clash = await admin.PostAsJsonAsync("/api/v1/permit-types", Valid("RESIDENTIAL BUILDING PERMIT"), Token);

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("permit_type.name_in_use", (await Problem.ReadAsync(clash, Token)).Code);
    }

    [Theory]
    [InlineData("Fortress", "permit_type.category")]
    [InlineData(null, "permit_type.category")]
    public async Task An_unknown_category_is_refused_with_its_code(string? category, string expectedCode)
    {
        using SeededApi api = new();
        using HttpClient admin = await api.SignInAsync(SeededApi.Administrator, Token);

        HttpResponseMessage response = await admin.PostAsJsonAsync("/api/v1/permit-types", Valid() with { Category = category }, Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(expectedCode, (await Problem.ReadAsync(response, Token)).Code);
    }

    [Theory]
    [InlineData("ab", 12_000, 100, 365, "permit_type.name")]
    [InlineData("Garden room permit", -1, 100, 365, "money.negative")]
    [InlineData("Garden room permit", 12_000, 5_000, 365, "permit_type.valuation_rate")]
    [InlineData("Garden room permit", 12_000, 100, 5, "permit_type.validity")]
    public async Task A_value_outside_the_limits_is_refused_with_the_rule_that_failed(
        string name, long feeCents, int basisPoints, int validityDays, string expectedCode)
    {
        using SeededApi api = new();
        using HttpClient admin = await api.SignInAsync(SeededApi.Administrator, Token);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/v1/permit-types", Valid(name) with { BaseFeeCents = feeCents, ValuationRateBasisPoints = basisPoints, ValidityDays = validityDays }, Token);

        Problem problem = await Problem.ReadAsync(response, Token);
        Assert.Equal(422, problem.Status);
        Assert.Equal(expectedCode, problem.Code);
        Assert.False(string.IsNullOrEmpty(problem.CorrelationId));
    }

    [Fact]
    public async Task Closing_a_type_hides_it_from_the_public_but_not_from_staff_and_it_can_be_reopened()
    {
        using SeededApi api = new();
        using HttpClient admin = await api.SignInAsync(SeededApi.Administrator, Token);
        using HttpClient anonymous = api.Anonymous();
        PermitTypeResponse banner = (await anonymous.GetFromJsonAsync<IReadOnlyList<PermitTypeResponse>>("/api/v1/permit-types", Token))!
            .Single(type => type.Name == "Temporary banner permit");

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/permit-types/{banner.Id}/close", null, Token)).StatusCode);

        Assert.Equal(8, (await anonymous.GetFromJsonAsync<IReadOnlyList<PermitTypeResponse>>("/api/v1/permit-types?includeInactive=true", Token))!.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/permit-types/{banner.Id}", Token)).StatusCode);
        Assert.Equal(9, (await admin.GetFromJsonAsync<IReadOnlyList<PermitTypeResponse>>("/api/v1/permit-types?includeInactive=true", Token))!.Count);
        Assert.False((await admin.GetFromJsonAsync<PermitTypeResponse>($"/api/v1/permit-types/{banner.Id}", Token))!.IsActive);

        await admin.PostAsync($"/api/v1/permit-types/{banner.Id}/open", null, Token);

        Assert.Equal(9, (await anonymous.GetFromJsonAsync<IReadOnlyList<PermitTypeResponse>>("/api/v1/permit-types", Token))!.Count);
    }

    [Fact]
    public async Task Changing_a_type_changes_what_later_applications_cost_but_not_those_already_submitted()
    {
        using SeededApi api = new();
        using HttpClient admin = await api.SignInAsync(SeededApi.Administrator, Token);
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        PermitTypeResponse fence = (await applicant.GetFromJsonAsync<IReadOnlyList<PermitTypeResponse>>("/api/v1/permit-types", Token))!
            .Single(type => type.Name == "Fence and small structure permit");

        ApplicationResponse before = await ApplicationApiTests.CreateAndSubmitAsync(applicant, fence.Id, 0, Token);
        Assert.Equal(6_000, before.FeeAssessedCents);

        PermitTypeRequest dearer = new("Building", fence.Name, fence.Description, 9_000, 0, false, 180, 5);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/permit-types/{fence.Id}", dearer, Token)).StatusCode);

        ApplicationResponse after = await ApplicationApiTests.CreateAndSubmitAsync(applicant, fence.Id, 0, Token);
        Assert.Equal(9_000, after.FeeAssessedCents);
        Assert.Equal(6_000, (await applicant.GetFromJsonAsync<ApplicationResponse>($"/api/v1/applications/{before.Id}", Token))!.FeeAssessedCents);
    }

    [Fact]
    public async Task An_unknown_type_is_not_found()
    {
        using SeededApi api = new();
        using HttpClient client = api.Anonymous();

        HttpResponseMessage response = await client.GetAsync($"/api/v1/permit-types/{Guid.NewGuid()}", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("permit_type.not_found", (await Problem.ReadAsync(response, Token)).Code);
    }
}
