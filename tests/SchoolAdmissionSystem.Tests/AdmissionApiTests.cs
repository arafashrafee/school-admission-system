using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using SchoolAdmissionSystem.Api.Data;
using SchoolAdmissionSystem.Api.Services;
using Xunit;

namespace SchoolAdmissionSystem.Tests;

// Exercise the assignment contract over real HTTP handlers and PostgreSQL.
public class AdmissionApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<AdmissionService> _app = new WebApplicationFactory<AdmissionService>()
        .WithWebHostBuilder(builder => builder.UseContentRoot(
            Path.GetFullPath("../../../../../backend", AppContext.BaseDirectory)));
    private HttpClient _client = null!;
    private readonly List<Guid> _classes = new();
    private const string Student = "00000000000000001";
    private const string Other = "00000000000000002";
    private const string ClassFields = "id,name,totalSeats,heldSeats,confirmedSeats,availableSeats";
    private const string HistoryFields = "holdId,classId,className,status,expiresAt";

    private ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")!).Options);

    public Task InitializeAsync()
    {
        var liveUrl = Environment.GetEnvironmentVariable("ADMISSION_LIVE_URL");
        _client = string.IsNullOrEmpty(liveUrl) ? _app.CreateClient()
            : new HttpClient(new SocketsHttpHandler
            {
                // Send the invalid Unicode header fixture over the wire so the server's
                // rejection is exercised rather than HttpClient's ASCII-only guard.
                RequestHeaderEncodingSelector = (name, _) => name == "X-Student-Id" ? Encoding.UTF8 : null
            }) { BaseAddress = new Uri(liveUrl) };
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await using var db = Db();
        await db.AdmissionHolds.Where(h => _classes.Contains(h.ClassId)).ExecuteDeleteAsync();
        await db.AdmissionClasses.Where(c => _classes.Contains(c.Id)).ExecuteDeleteAsync();
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? student = Student, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (student != null) request.Headers.TryAddWithoutValidation("X-Student-Id", student);
        if (body != null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private static void Fields(JsonElement body, string names)
        => body.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(names.Split(','));

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected)
    {
        var json = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath} => {(int)response.StatusCode} {json}");
        response.StatusCode.Should().Be(expected, json);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateClass(int seats = 1, string? name = null)
    {
        var response = await _client.PostAsJsonAsync("/classes", new
            { name = name ?? $"API-{Guid.NewGuid():N}", totalSeats = seats });
        var body = await Read(response, HttpStatusCode.Created);
        var id = body.GetProperty("id").GetGuid();
        _classes.Add(id);
        Fields(body, "id,name,totalSeats");
        body.GetProperty("id").ValueKind.Should().Be(JsonValueKind.String);
        response.Headers.Location!.ToString().Should().Be($"/classes/{id}");
        return id;
    }

    private async Task<Guid> Hold(Guid id, string student = Student)
    {
        var response = await Send(HttpMethod.Post, $"/classes/{id}/holds", student);
        var body = await Read(response, HttpStatusCode.Created);
        Fields(body, "holdId,classId,studentId,status,expiresAt");
        var holdId = body.GetProperty("holdId").GetGuid();
        body.GetProperty("classId").GetGuid().Should().Be(id);
        body.GetProperty("studentId").GetString().Should().Be(student);
        body.GetProperty("status").GetString().Should().Be("HELD");
        body.GetProperty("expiresAt").GetDateTime().Kind.Should().Be(DateTimeKind.Utc);
        body.GetProperty("expiresAt").GetDateTime().Should().BeAfter(DateTime.UtcNow);
        response.Headers.Location!.ToString().Should().Be($"/holds/{holdId}");
        return holdId;
    }

    [Fact]
    public async Task AllEightApis_ExactResponses_StableIds_AndIdempotency()
    {
        var id = await CreateClass();
        var hold = await Hold(id);
        var status = await Read(await _client.GetAsync($"/holds/{hold}"), HttpStatusCode.OK);
        Fields(status, "holdId,classId,status,expiresAt");
        status.GetProperty("holdId").GetGuid().Should().Be(hold);
        status.GetProperty("classId").GetGuid().Should().Be(id);
        status.GetProperty("status").GetString().Should().Be("HELD");
        var counts = await Read(await _client.GetAsync($"/classes/{id}"), HttpStatusCode.OK);
        Fields(counts, ClassFields);
        counts.GetProperty("heldSeats").GetInt32().Should().Be(1);
        counts.GetProperty("availableSeats").GetInt32().Should().Be(0);
        var classes = await Read(await _client.GetAsync("/classes"), HttpStatusCode.OK);
        foreach (var row in classes.EnumerateArray()) Fields(row, ClassFields);
        classes.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id)
            .GetProperty("heldSeats").GetInt32().Should().Be(1);
        var history = await Read(await Send(HttpMethod.Get, "/me/holds"), HttpStatusCode.OK);
        foreach (var row in history.EnumerateArray()) Fields(row, HistoryFields);
        history.EnumerateArray().Should().Contain(h => h.GetProperty("holdId").GetGuid() == hold);
        for (var i = 0; i < 2; i++)
        {
            var cancelled = await Send(HttpMethod.Delete, $"/holds/{hold}");
            cancelled.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await cancelled.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        }
        await Read(await Send(HttpMethod.Post, $"/holds/{hold}/confirm"), HttpStatusCode.Conflict);
        var next = await Hold(id);
        for (var i = 0; i < 2; i++)
        {
            var confirmed = await Read(await Send(HttpMethod.Post, $"/holds/{next}/confirm"), HttpStatusCode.OK);
            Fields(confirmed, "holdId,status");
            confirmed.GetProperty("holdId").GetGuid().Should().Be(next);
            confirmed.GetProperty("status").GetString().Should().Be("CONFIRMED");
        }
        counts = await Read(await _client.GetAsync($"/classes/{id}"), HttpStatusCode.OK);
        counts.GetProperty("confirmedSeats").GetInt32().Should().Be(1);
        counts.GetProperty("heldSeats").GetInt32().Should().Be(0);
        await Read(await Send(HttpMethod.Delete, $"/holds/{next}"), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(null, 1)] [InlineData("", 1)] [InlineData("   ", 1)]
    [InlineData("Valid", 0)] [InlineData("Valid", -1)]
    public async Task CreateClass_RejectsInvalidInput(string? name, int totalSeats)
        => await Read(await _client.PostAsJsonAsync("/classes", new { name, totalSeats }), HttpStatusCode.BadRequest);

    [Fact]
    public async Task CreateClass_TrimsName_RejectsOverlongAndDuplicateNamesIncludingRaces()
    {
        await Read(await _client.PostAsJsonAsync("/classes", new { name = new string('a', 101), totalSeats = 1 }), HttpStatusCode.BadRequest);
        var name = $"API-{Guid.NewGuid():N}";
        var id = await CreateClass(name: $" {name} ");
        var row = await Read(await _client.GetAsync($"/classes/{id}"), HttpStatusCode.OK);
        row.GetProperty("name").GetString().Should().Be(name);
        await Read(await _client.PostAsJsonAsync("/classes", new { name, totalSeats = 1 }), HttpStatusCode.Conflict);
        var raceName = $"API-{Guid.NewGuid():N}";
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            _client.PostAsJsonAsync("/classes", new { name = raceName, totalSeats = 1 })));
        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Created))
            _classes.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(7);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("123")] [InlineData("1234567890123456a")]
    [InlineData("１２３４５６７８９０１２３４５６７")] [InlineData("123456789012345678")]
    public async Task AllIdentityEndpoints_RejectInvalidOrMissingHeaders_EvenWithBodyIdentity(string? studentId)
    {
        var id = Guid.NewGuid();
        var body = new { studentId = Student };
        await Read(await Send(HttpMethod.Post, $"/classes/{id}/holds", studentId, body), HttpStatusCode.BadRequest);
        await Read(await Send(HttpMethod.Post, $"/holds/{id}/confirm", studentId, body), HttpStatusCode.BadRequest);
        await Read(await Send(HttpMethod.Delete, $"/holds/{id}", studentId, body), HttpStatusCode.BadRequest);
        await Read(await Send(HttpMethod.Get, "/me/holds", studentId), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Errors_MapMissingOwnershipDuplicateAndFullToHttpStatuses()
    {
        var missing = Guid.NewGuid();
        foreach (var path in new[] { $"/classes/{missing}", $"/holds/{missing}" })
            await Read(await _client.GetAsync(path), HttpStatusCode.NotFound);
        await Read(await Send(HttpMethod.Post, $"/classes/{missing}/holds"), HttpStatusCode.NotFound);
        await Read(await Send(HttpMethod.Post, $"/holds/{missing}/confirm"), HttpStatusCode.NotFound);
        await Read(await Send(HttpMethod.Delete, $"/holds/{missing}"), HttpStatusCode.NotFound);
        var id = await CreateClass();
        var hold = await Hold(id);
        foreach (var student in new[] { Student, Other })
            await Read(await Send(HttpMethod.Post, $"/classes/{id}/holds", student), HttpStatusCode.Conflict);
        await Read(await Send(HttpMethod.Post, $"/holds/{hold}/confirm", Other, new { studentId = Student }), HttpStatusCode.Forbidden);
        await Read(await Send(HttpMethod.Delete, $"/holds/{hold}", Other, new { studentId = Student }), HttpStatusCode.Forbidden);
        var malformed = await _client.PostAsync("/classes", new StringContent("{", Encoding.UTF8, "application/json"));
        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalidId = await Send(HttpMethod.Post, "/classes/not-a-guid/holds");
        ((int)invalidId.StatusCode).Should().BeInRange(400, 499);
    }

    [Fact]
    public async Task MeHolds_IsolatesStudents_AndIgnoresBodyAndQueryIdentity()
    {
        var id = await CreateClass(2);
        var first = await Hold(id);
        var other = await Hold(id, Other);
        foreach (var pair in new[] { (Student, first, other), (Other, other, first) })
        {
            var history = await Read(await Send(HttpMethod.Get, $"/me/holds?studentId={pair.Item1}", pair.Item1), HttpStatusCode.OK);
            history.EnumerateArray().Should().Contain(h => h.GetProperty("holdId").GetGuid() == pair.Item2);
            history.EnumerateArray().Should().NotContain(h => h.GetProperty("holdId").GetGuid() == pair.Item3);
        }
        var isolated = await Read(await Send(HttpMethod.Get, $"/me/holds?studentId={Other}"), HttpStatusCode.OK);
        isolated.EnumerateArray().Should().NotContain(h => h.GetProperty("holdId").GetGuid() == other);
        var empty = await Read(await Send(HttpMethod.Get, "/me/holds", "99999999999999999"), HttpStatusCode.OK);
        empty.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task ClockExpiredHolds_AreReportedExpiredAndReleaseCapacityBeforeSweep()
    {
        var id = await CreateClass();
        var hold = await Hold(id);
        await using var db = Db();
        await db.AdmissionHolds.Where(h => h.Id == hold)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        var status = await Read(await _client.GetAsync($"/holds/{hold}"), HttpStatusCode.OK);
        Fields(status, "holdId,classId,status,expiresAt");
        status.GetProperty("status").GetString().Should().Be("EXPIRED");
        var history = await Read(await Send(HttpMethod.Get, "/me/holds"), HttpStatusCode.OK);
        history.EnumerateArray().Single(h => h.GetProperty("holdId").GetGuid() == hold)
            .GetProperty("status").GetString().Should().Be("EXPIRED");
        var classes = await Read(await _client.GetAsync("/classes"), HttpStatusCode.OK);
        classes.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id)
            .GetProperty("availableSeats").GetInt32().Should().Be(1);
        await Read(await Send(HttpMethod.Post, $"/holds/{hold}/confirm"), HttpStatusCode.Conflict);
        await Read(await Send(HttpMethod.Delete, $"/holds/{hold}"), HttpStatusCode.Conflict);
        await Hold(id);
    }

    [Fact]
    public async Task HttpConcurrency_100RequestsFor10Seats_Exactly10Created90Conflict()
    {
        var id = await CreateClass(10);
        var responses = await Task.WhenAll(Enumerable.Range(0, 100).Select(i =>
            Send(HttpMethod.Post, $"/classes/{id}/holds", i.ToString("D17"))));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(10);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(90);
        var ids = new HashSet<Guid>();
        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Created))
        {
            var body = await Read(response, HttpStatusCode.Created);
            Fields(body, "holdId,classId,studentId,status,expiresAt");
            ids.Add(body.GetProperty("holdId").GetGuid()).Should().BeTrue();
        }
        var counts = await Read(await _client.GetAsync($"/classes/{id}"), HttpStatusCode.OK);
        counts.GetProperty("heldSeats").GetInt32().Should().Be(10);
        counts.GetProperty("availableSeats").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ValidHeaderWinsOverBodyIdentity_AndMultipleHeadersAreRejected()
    {
        var id = await CreateClass(2);
        var body = await Read(await Send(HttpMethod.Post, $"/classes/{id}/holds", Student,
            new { studentId = Other }), HttpStatusCode.Created);
        body.GetProperty("studentId").GetString().Should().Be(Student);
        var hold = body.GetProperty("holdId").GetGuid();
        var confirmed = await Read(await Send(HttpMethod.Post, $"/holds/{hold}/confirm", Student,
            new { studentId = Other }), HttpStatusCode.OK);
        Fields(confirmed, "holdId,status");
        var request = new HttpRequestMessage(HttpMethod.Get, "/me/holds");
        request.Headers.TryAddWithoutValidation("X-Student-Id", new[] { Student, Other });
        await Read(await _client.SendAsync(request), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("POST", "/holds/{0}")]
    [InlineData("DELETE", "/holds/{0}/confirm")]
    [InlineData("GET", "/classes/{0}/holds")]
    [InlineData("POST", "/me/holds")]
    public async Task ExactPaths_RejectIncorrectMethods(string method, string path)
    {
        var response = await Send(new HttpMethod(method), string.Format(path, Guid.NewGuid()));
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Swagger_ContainsExactlyAssignmentBusinessRoutesAndMethods()
    {
        var spec = await Read(await _client.GetAsync("/swagger/v1/swagger.json"), HttpStatusCode.OK);
        var operations = spec.GetProperty("paths").EnumerateObject()
            .Where(p => p.Name != "/" && p.Name != "/health")
            .SelectMany(p => p.Value.EnumerateObject().Select(m => $"{m.Name.ToUpperInvariant()} {p.Name}"));
        foreach (var endpoint in new[] { ("/classes/{classId}/holds", "post"), ("/holds/{holdId}/confirm", "post"),
            ("/holds/{holdId}", "delete"), ("/me/holds", "get") })
        {
            var operation = spec.GetProperty("paths").GetProperty(endpoint.Item1).GetProperty(endpoint.Item2);
            var header = operation.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "X-Student-Id");
            header.GetProperty("in").GetString().Should().Be("header");
            header.GetProperty("required").GetBoolean().Should().BeTrue();
            operation.TryGetProperty("requestBody", out _).Should().BeFalse();
        }
        operations.Should().BeEquivalentTo(new[] { "POST /classes", "GET /classes", "GET /classes/{classId}",
            "POST /classes/{classId}/holds", "GET /holds/{holdId}", "POST /holds/{holdId}/confirm",
            "DELETE /holds/{holdId}", "GET /me/holds" });
    }
}
