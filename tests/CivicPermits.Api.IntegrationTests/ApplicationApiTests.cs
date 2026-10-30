using System.Net;
using CivicPermits.Infrastructure.Persistence.Seeding;
using CivicPermits.Shared.Contracts;

namespace CivicPermits.Api.IntegrationTests;

public class ApplicationApiTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly AddressDto Site = new("21 Quarry Lane", null, "Bramblewick", "TG", "70002");

    internal static async Task<Guid> TypeIdAsync(HttpClient client, string name, CancellationToken cancellationToken) =>
        (await client.GetFromJsonAsync<IReadOnlyList<PermitTypeResponse>>("/api/v1/permit-types", cancellationToken))!
            .Single(type => type.Name == name).Id;

    internal static async Task<ApplicationResponse> CreateDraftAsync(HttpClient applicant, Guid typeId, long valuationCents, CancellationToken cancellationToken)
    {
        HttpResponseMessage created = await applicant.PostAsJsonAsync(
            "/api/v1/applications",
            new CreateApplicationRequest(typeId, "Build a timber pergola over the back patio.", Site, valuationCents),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (await created.Content.ReadFromJsonAsync<ApplicationResponse>(cancellationToken))!;
    }

    internal static async Task<ApplicationResponse> CreateAndSubmitAsync(HttpClient applicant, Guid typeId, long valuationCents, CancellationToken cancellationToken)
    {
        ApplicationResponse draft = await CreateDraftAsync(applicant, typeId, valuationCents, cancellationToken);

        return await StepAsync(applicant, draft.Id, "submit", null, cancellationToken);
    }

    internal static async Task<ApplicationResponse> StepAsync(HttpClient client, Guid id, string step, string? reason, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/applications/{id}/{step}", new ReasonRequest(reason), cancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"{step} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");

        return (await response.Content.ReadFromJsonAsync<ApplicationResponse>(cancellationToken))!;
    }

    /// <summary>The number the next submission should get: one past the highest already used this year.</summary>
    private static async Task<string> NextNumberAsync(HttpClient staff, CancellationToken cancellationToken)
    {
        int year = DateTimeOffset.UtcNow.Year;
        string prefix = $"CP-{year}-";
        PagedResponse<ApplicationSummaryResponse> all = (await staff.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications?pageSize=100", cancellationToken))!;
        int highest = all.Items
            .Where(item => item.Number?.StartsWith(prefix, StringComparison.Ordinal) == true)
            .Select(item => int.Parse(item.Number![prefix.Length..], System.Globalization.CultureInfo.InvariantCulture))
            .Max();

        return prefix + (highest + 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task An_application_goes_from_draft_to_an_issued_permit_that_the_public_can_look_up()
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient inspector = await api.SignInAsync(SeededApi.InspectorOne, Token);
        using HttpClient anonymous = api.Anonymous();
        Guid residential = await TypeIdAsync(applicant, "Residential building permit", Token);

        // The applicant makes a draft, corrects it, and sends it.
        ApplicationResponse draft = await CreateDraftAsync(applicant, residential, 24_000_00, Token);
        Assert.Equal("Draft", draft.Status);
        Assert.Null(draft.Number);
        Assert.Equal(["Submit", "Withdraw"], draft.AllowedActions);

        HttpResponseMessage revised = await applicant.PutAsJsonAsync(
            $"/api/v1/applications/{draft.Id}",
            new ReviseApplicationRequest("Build a covered timber pergola over the back patio.", Site, 30_000_00),
            Token);
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);

        ApplicationResponse submitted = await StepAsync(applicant, draft.Id, "submit", null, Token);
        Assert.Equal("Submitted", submitted.Status);
        Assert.Matches(@"^CP-\d{4}-\d{6}$", submitted.Number);
        Assert.Equal(52_500, submitted.FeeAssessedCents);

        // Staff review and approve.
        Assert.Equal("InReview", (await StepAsync(clerk, draft.Id, "start-review", null, Token)).Status);
        ApplicationResponse approved = await StepAsync(clerk, draft.Id, "approve", "Plans comply.", Token);
        Assert.Equal("Approved", approved.Status);
        Assert.Equal("Plans comply.", approved.DecisionNote);

        // The permit needs the fee and an inspection before it can be issued.
        HttpResponseMessage tooEarly = await clerk.PostAsJsonAsync($"/api/v1/applications/{draft.Id}/issue", new ReasonRequest(null), Token);
        Assert.Equal("issue.fee_unpaid", (await Problem.ReadAsync(tooEarly, Token)).Code);

        HttpResponseMessage paid = await clerk.PostAsync($"/api/v1/applications/{draft.Id}/payment", null, Token);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);

        HttpResponseMessage stillNoInspection = await clerk.PostAsJsonAsync($"/api/v1/applications/{draft.Id}/issue", new ReasonRequest(null), Token);
        Assert.Equal("issue.inspection_not_passed", (await Problem.ReadAsync(stillNoInspection, Token)).Code);

        HttpResponseMessage booked = await clerk.PostAsJsonAsync(
            $"/api/v1/applications/{draft.Id}/inspections",
            new ScheduleInspectionRequest(SampleIdentities.InspectorOne, DateTimeOffset.UtcNow.AddDays(3)),
            Token);
        ApplicationResponse withInspection = (await booked.Content.ReadFromJsonAsync<ApplicationResponse>(Token))!;
        InspectionResponse inspection = Assert.Single(withInspection.Inspections);

        HttpResponseMessage passed = await inspector.PostAsJsonAsync(
            $"/api/v1/applications/{draft.Id}/inspections/{inspection.Id}/result", new InspectionResultRequest(true, "Footings and framing are sound."), Token);
        Assert.Equal(HttpStatusCode.OK, passed.StatusCode);

        ApplicationResponse issued = await StepAsync(clerk, draft.Id, "issue", null, Token);
        Assert.Equal("Issued", issued.Status);
        Assert.NotNull(issued.ExpiresOn);
        Assert.Equal(["Submit", "StartReview", "Approve", "Issue"], issued.History.Select(change => change.Action));

        // Anyone can look the permit up by number and learns nothing about the applicant or the site.
        HttpResponseMessage lookup = await anonymous.GetAsync($"/api/v1/public/permits/{issued.Number}", Token);
        string body = await lookup.Content.ReadAsStringAsync(Token);
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        Assert.Contains("\"status\":\"Issued\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Quarry", body, StringComparison.Ordinal);
        Assert.DoesNotContain("pergola", body, StringComparison.Ordinal);
        Assert.DoesNotContain(draft.Id.ToString(), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Numbers_are_handed_out_in_order_and_continue_after_the_sample_data()
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        Guid fence = await TypeIdAsync(applicant, "Fence and small structure permit", Token);
        string expected = await NextNumberAsync(clerk, Token);

        ApplicationResponse first = await CreateAndSubmitAsync(applicant, fence, 0, Token);
        ApplicationResponse second = await CreateAndSubmitAsync(applicant, fence, 0, Token);

        Assert.Equal(expected, first.Number);
        Assert.Equal(int.Parse(expected[^6..], System.Globalization.CultureInfo.InvariantCulture) + 1, int.Parse(second.Number![^6..], System.Globalization.CultureInfo.InvariantCulture));
        Assert.EndsWith("000012", expected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_applicant_cannot_see_or_touch_someone_elses_application()
    {
        using SeededApi api = new();
        using HttpClient owner = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient other = await api.SignInAsync(SeededApi.ApplicantTwo, Token);
        ApplicationResponse draft = await CreateDraftAsync(owner, await TypeIdAsync(owner, "Fence and small structure permit", Token), 0, Token);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/applications/{draft.Id}", Token)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync($"/api/v1/applications/{draft.Id}", new ReviseApplicationRequest("Something else entirely here.", Site, 0), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/applications/{draft.Id}/submit", new ReasonRequest(null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/applications/{draft.Id}/withdraw", new ReasonRequest(null), Token)).StatusCode);

        // Not found, never forbidden: the answer must not confirm the application exists.
        HttpResponseMessage missing = await other.GetAsync($"/api/v1/applications/{Guid.NewGuid()}", Token);
        HttpResponseMessage hidden = await other.GetAsync($"/api/v1/applications/{draft.Id}", Token);
        Assert.Equal((await Problem.ReadAsync(missing, Token)).Code, (await Problem.ReadAsync(hidden, Token)).Code);
        Assert.Equal("Draft", (await owner.GetFromJsonAsync<ApplicationResponse>($"/api/v1/applications/{draft.Id}", Token))!.Status);
    }

    [Fact]
    public async Task Only_applicants_can_start_applications_and_staff_cannot_stand_in_for_them()
    {
        using SeededApi api = new();
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        Guid fence = await TypeIdAsync(applicant, "Fence and small structure permit", Token);
        ApplicationResponse draft = await CreateDraftAsync(applicant, fence, 0, Token);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await clerk.PostAsJsonAsync("/api/v1/applications", new CreateApplicationRequest(fence, "Build a fence of cedar.", Site, 0), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsJsonAsync($"/api/v1/applications/{draft.Id}/submit", new ReasonRequest(null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsJsonAsync($"/api/v1/applications/{draft.Id}/withdraw", new ReasonRequest(null), Token)).StatusCode);
    }

    [Fact]
    public async Task An_applicant_cannot_review_decide_or_record_payment_on_their_own_application()
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        ApplicationResponse submitted = await CreateAndSubmitAsync(applicant, await TypeIdAsync(applicant, "Fence and small structure permit", Token), 0, Token);

        // Every staff step is closed to an applicant at the endpoint, whether or not the workflow would allow it now.
        foreach (string step in new[] { "start-review", "approve", "deny", "request-corrections", "issue" })
        {
            HttpResponseMessage response = await applicant.PostAsJsonAsync($"/api/v1/applications/{submitted.Id}/{step}", new ReasonRequest("Because I would like it."), Token);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.PostAsync($"/api/v1/applications/{submitted.Id}/payment", null, Token)).StatusCode);
        Assert.Equal("Submitted", (await applicant.GetFromJsonAsync<ApplicationResponse>($"/api/v1/applications/{submitted.Id}", Token))!.Status);
    }

    [Fact]
    public async Task Denying_and_asking_for_corrections_need_a_reason_the_applicant_can_read()
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        ApplicationResponse submitted = await CreateAndSubmitAsync(applicant, await TypeIdAsync(applicant, "Fence and small structure permit", Token), 0, Token);
        await StepAsync(clerk, submitted.Id, "start-review", null, Token);

        HttpResponseMessage noReason = await clerk.PostAsJsonAsync($"/api/v1/applications/{submitted.Id}/deny", new ReasonRequest(null), Token);
        Problem problem = await Problem.ReadAsync(noReason, Token);
        Assert.Equal(422, problem.Status);
        Assert.Equal("application.reason_required", problem.Code);

        await StepAsync(clerk, submitted.Id, "request-corrections", "Please add the lot boundary to the plan.", Token);
        ApplicationResponse seenByApplicant = (await applicant.GetFromJsonAsync<ApplicationResponse>($"/api/v1/applications/{submitted.Id}", Token))!;

        Assert.Equal("CorrectionsRequested", seenByApplicant.Status);
        Assert.Equal("Please add the lot boundary to the plan.", seenByApplicant.History[^1].Note);
        Assert.Equal(["Submit", "Withdraw"], seenByApplicant.AllowedActions);

        // The applicant fixes it and sends it again; the number does not change.
        ApplicationResponse resubmitted = await StepAsync(applicant, submitted.Id, "submit", null, Token);
        Assert.Equal(submitted.Number, resubmitted.Number);
        Assert.Equal("Submitted", resubmitted.Status);
    }

    [Fact]
    public async Task A_step_the_workflow_does_not_allow_is_a_conflict_and_changes_nothing()
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        ApplicationResponse draft = await CreateDraftAsync(applicant, await TypeIdAsync(applicant, "Fence and small structure permit", Token), 0, Token);

        HttpResponseMessage response = await clerk.PostAsJsonAsync($"/api/v1/applications/{draft.Id}/approve", new ReasonRequest(null), Token);

        Problem problem = await Problem.ReadAsync(response, Token);
        Assert.Equal(409, problem.Status);
        Assert.Equal("workflow.transition_not_allowed", problem.Code);
        Assert.Empty((await clerk.GetFromJsonAsync<ApplicationResponse>($"/api/v1/applications/{draft.Id}", Token))!.History);
    }

    [Fact]
    public async Task A_closed_permit_type_cannot_be_applied_for()
    {
        using SeededApi api = new();
        using HttpClient admin = await api.SignInAsync(SeededApi.Administrator, Token);
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        Guid banner = await TypeIdAsync(applicant, "Temporary banner permit", Token);
        await admin.PostAsync($"/api/v1/permit-types/{banner}/close", null, Token);

        HttpResponseMessage response = await applicant.PostAsJsonAsync(
            "/api/v1/applications", new CreateApplicationRequest(banner, "Hang a banner above the shop door.", Site, 0), Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application.permit_type_retired", (await Problem.ReadAsync(response, Token)).Code);
    }

    [Theory]
    [InlineData("short", "70002", 0, "application.description")]
    [InlineData("Build a timber pergola over the patio.", "7000", 0, "address.postal_code")]
    [InlineData("Build a timber pergola over the patio.", "70002", -5, "money.negative")]
    public async Task A_bad_value_in_an_application_is_refused_with_its_code(string description, string postalCode, long valuation, string expectedCode)
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        Guid fence = await TypeIdAsync(applicant, "Fence and small structure permit", Token);

        HttpResponseMessage response = await applicant.PostAsJsonAsync(
            "/api/v1/applications",
            new CreateApplicationRequest(fence, description, Site with { PostalCode = postalCode }, valuation),
            Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(expectedCode, (await Problem.ReadAsync(response, Token)).Code);
    }

    [Fact]
    public async Task A_missing_address_is_refused_not_a_server_error()
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        Guid fence = await TypeIdAsync(applicant, "Fence and small structure permit", Token);

        HttpResponseMessage response = await applicant.PostAsJsonAsync(
            "/api/v1/applications", new CreateApplicationRequest(fence, "Build a timber pergola over the patio.", null, 0), Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("address.line1", (await Problem.ReadAsync(response, Token)).Code);
    }

    [Fact]
    public async Task A_permit_that_depends_on_value_cannot_be_sent_without_one()
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        ApplicationResponse draft = await CreateDraftAsync(applicant, await TypeIdAsync(applicant, "Residential building permit", Token), 0, Token);
        string expected = await NextNumberAsync(clerk, Token);

        HttpResponseMessage response = await applicant.PostAsJsonAsync($"/api/v1/applications/{draft.Id}/submit", new ReasonRequest(null), Token);

        Assert.Equal("application.valuation_required", (await Problem.ReadAsync(response, Token)).Code);

        // The failed attempt did not use up a permit number.
        Guid fence = await TypeIdAsync(applicant, "Fence and small structure permit", Token);
        Assert.Equal(expected, (await CreateAndSubmitAsync(applicant, fence, 0, Token)).Number);
    }

    [Fact]
    public async Task Inspections_can_only_be_booked_for_real_inspectors_and_only_the_assigned_one_can_record_the_result()
    {
        using SeededApi api = new();
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient assigned = await api.SignInAsync(SeededApi.InspectorOne, Token);
        using HttpClient otherInspector = await api.SignInAsync(SeededApi.InspectorTwo, Token);
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);

        // Sample application 5 (the kitchen remodel) is approved with an inspection booked for inspector one.
        ApplicationSummaryResponse kitchen = (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>(
            "/api/v1/applications?status=Approved&pageSize=50", Token))!.Items.Single(item => item.ProjectDescription.StartsWith("Remodel the kitchen", StringComparison.Ordinal));
        ApplicationResponse full = (await clerk.GetFromJsonAsync<ApplicationResponse>($"/api/v1/applications/{kitchen.Id}", Token))!;
        InspectionResponse booked = Assert.Single(full.Inspections);

        // An applicant is not an inspector, so cannot be given an inspection.
        ApplicationSummaryResponse stall = (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>(
            "/api/v1/applications?status=Approved&pageSize=50", Token))!.Items.Single(item => item.ProjectDescription.StartsWith("A produce stall", StringComparison.Ordinal));
        HttpResponseMessage notAnInspector = await clerk.PostAsJsonAsync(
            $"/api/v1/applications/{stall.Id}/inspections", new ScheduleInspectionRequest(SampleIdentities.ApplicantOne, DateTimeOffset.UtcNow.AddDays(2)), Token);
        Assert.Equal("inspection.inspector", (await Problem.ReadAsync(notAnInspector, Token)).Code);

        // The other inspector cannot even see the application; the assigned one can.
        Assert.Equal(HttpStatusCode.NotFound, (await otherInspector.GetAsync($"/api/v1/applications/{kitchen.Id}", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherInspector.PostAsJsonAsync(
            $"/api/v1/applications/{kitchen.Id}/inspections/{booked.Id}/result", new InspectionResultRequest(true, null), Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.PostAsJsonAsync(
            $"/api/v1/applications/{kitchen.Id}/inspections/{booked.Id}/result", new InspectionResultRequest(true, null), Token)).StatusCode);

        HttpResponseMessage failedWithoutNotes = await assigned.PostAsJsonAsync(
            $"/api/v1/applications/{kitchen.Id}/inspections/{booked.Id}/result", new InspectionResultRequest(false, null), Token);
        Assert.Equal("inspection.failure_needs_notes", (await Problem.ReadAsync(failedWithoutNotes, Token)).Code);

        HttpResponseMessage failed = await assigned.PostAsJsonAsync(
            $"/api/v1/applications/{kitchen.Id}/inspections/{booked.Id}/result", new InspectionResultRequest(false, "The new beam is not bearing on the post."), Token);
        Assert.Equal("Failed", (await failed.Content.ReadFromJsonAsync<ApplicationResponse>(Token))!.Inspections.Single().Status);
    }

    [Fact]
    public async Task Staff_can_list_the_inspectors_by_name_and_nobody_else_can()
    {
        using SeededApi api = new();
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient inspector = await api.SignInAsync(SeededApi.InspectorOne, Token);
        using HttpClient anonymous = api.Anonymous();

        IReadOnlyList<InspectorResponse> inspectors = (await clerk.GetFromJsonAsync<IReadOnlyList<InspectorResponse>>("/api/v1/inspectors", Token))!;

        Assert.Equal(["Sample Inspector One", "Sample Inspector Two"], inspectors.Select(person => person.DisplayName));
        Assert.Contains(inspectors, person => person.Id == SampleIdentities.InspectorOne);
        Assert.Equal(HttpStatusCode.Forbidden, (await applicant.GetAsync("/api/v1/inspectors", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await inspector.GetAsync("/api/v1/inspectors", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/inspectors", Token)).StatusCode);
    }

    [Fact]
    public async Task Searching_shows_each_person_only_what_they_are_allowed_to_see()
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        using HttpClient inspector = await api.SignInAsync(SeededApi.InspectorOne, Token);

        PagedResponse<ApplicationSummaryResponse> mine = (await applicant.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications", Token))!;
        PagedResponse<ApplicationSummaryResponse> everything = (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications?pageSize=100", Token))!;
        PagedResponse<ApplicationSummaryResponse> inspections = (await inspector.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications", Token))!;

        Assert.Equal(4, mine.TotalCount);
        Assert.Equal(12, everything.TotalCount);
        Assert.Equal(2, inspections.TotalCount);
        Assert.True(everything.Items.Select(item => item.CreatedOn).SequenceEqual(everything.Items.Select(item => item.CreatedOn).OrderDescending()));

        PagedResponse<ApplicationSummaryResponse> issued = (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications?status=Issued", Token))!;
        Assert.Equal(2, issued.TotalCount);
        Assert.All(issued.Items, item => Assert.Equal("Issued", item.Status));

        PagedResponse<ApplicationSummaryResponse> secondPage = (await clerk.GetFromJsonAsync<PagedResponse<ApplicationSummaryResponse>>("/api/v1/applications?page=2&pageSize=5", Token))!;
        Assert.Equal(5, secondPage.Items.Count);
        Assert.Equal(12, secondPage.TotalCount);
        Assert.Empty(secondPage.Items.Select(item => item.Id).Intersect(everything.Items.Take(5).Select(item => item.Id)));
    }

    [Theory]
    [InlineData("page=0", "paging.page")]
    [InlineData("pageSize=0", "paging.page_size")]
    [InlineData("pageSize=101", "paging.page_size")]
    public async Task A_bad_page_request_is_refused(string query, string expectedCode)
    {
        using SeededApi api = new();
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);

        HttpResponseMessage response = await clerk.GetAsync($"/api/v1/applications?{query}", Token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(expectedCode, (await Problem.ReadAsync(response, Token)).Code);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_client_error_not_a_server_error()
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using StringContent junk = new("{ not json", System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response = await applicant.PostAsync("/api/v1/applications", junk, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_viewer_is_only_offered_steps_they_could_take()
    {
        using SeededApi api = new();
        using HttpClient applicant = await api.SignInAsync(SeededApi.ApplicantOne, Token);
        using HttpClient clerk = await api.SignInAsync(SeededApi.Clerk, Token);
        ApplicationResponse submitted = await CreateAndSubmitAsync(applicant, await TypeIdAsync(applicant, "Fence and small structure permit", Token), 0, Token);

        Assert.Equal(["Withdraw"], (await applicant.GetFromJsonAsync<ApplicationResponse>($"/api/v1/applications/{submitted.Id}", Token))!.AllowedActions);
        Assert.Equal(["StartReview"], (await clerk.GetFromJsonAsync<ApplicationResponse>($"/api/v1/applications/{submitted.Id}", Token))!.AllowedActions);
    }
}
