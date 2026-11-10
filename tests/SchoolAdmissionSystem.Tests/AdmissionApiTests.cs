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

// Real HTTP handlers, services and PostgreSQL; each test owns and removes only its classes.
public class AdmissionApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<AdmissionService> _app = new WebApplicationFactory<AdmissionService>()
        .WithWebHostBuilder(builder => builder.UseContentRoot(
            Path.GetFullPath("../../../../../backend", AppContext.BaseDirectory)));
    private HttpClient _client = null!;
    private readonly List<Guid> _classes = new();
    private const string Student = "00000000000000001";
    private const string Other = "00000000000000002";

    public Task InitializeAsync()
    {
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=postgres;Database=school_admission;Username=postgres;Password=postgres").Options;
        await using var db = new ApplicationDbContext(options);
        await db.AdmissionHolds.Where(h => _classes.Contains(h.ClassId)).ExecuteDeleteAsync();
        await db.AdmissionClasses.Where(c => _classes.Contains(c.Id)).ExecuteDeleteAsync();
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateClass(int seats = 1, string? name = null)
    {
        var body = await Read(await _client.PostAsJsonAsync("/api/classes", new
        { name = name ?? $"API-{Guid.NewGuid():N}", totalSeats = seats }), HttpStatusCode.Created);
        var id = body.GetProperty("classId").GetGuid();
        _classes.Add(id);
        return id;
    }

    private async Task<Guid> Hold(Guid id, string student = Student)
    {
        var response = await _client.PostAsJsonAsync("/api/holds", new { classId = id, studentId = student });
        response.Headers.Location.Should().NotBeNull();
        var body = await Read(response, HttpStatusCode.Created);
        return body.GetProperty("holdId").GetGuid();
    }

    [Fact]
    public async Task AllEightApis_CompleteLifecycle_PreserveLeadingZerosAndSeatCounts()
    {
        var id = await CreateClass();
        var hold = await Hold(id);
        var status = await Read(await _client.GetAsync($"/api/holds/{hold}"), HttpStatusCode.OK);
        status.GetProperty("studentId").GetString().Should().Be(Student);
        status.GetProperty("status").GetString().Should().Be("HELD");
        var counts = await Read(await _client.GetAsync($"/api/classes/{id}/seats"), HttpStatusCode.OK);
        counts.GetProperty("heldSeats").GetInt32().Should().Be(1);
        counts.GetProperty("availableSeats").GetInt32().Should().Be(0);
        var classes = await Read(await _client.GetAsync("/api/classes"), HttpStatusCode.OK);
        classes.EnumerateArray().Single(c => c.GetProperty("classId").GetGuid() == id)
            .GetProperty("heldSeats").GetInt32().Should().Be(1);
        var history = await Read(await _client.GetAsync($"/api/students/{Student}/holds"), HttpStatusCode.OK);
        history.EnumerateArray().Should().Contain(h => h.GetProperty("holdId").GetGuid() == hold);
        await Read(await _client.PostAsJsonAsync($"/api/holds/{hold}/cancel", new { studentId = Student }), HttpStatusCode.OK);
        var next = await Hold(id);
        for (var i = 0; i < 2; i++)
        {
            var confirmed = await Read(await _client.PostAsJsonAsync($"/api/holds/{next}/confirm", new { studentId = Student }), HttpStatusCode.OK);
            confirmed.GetProperty("status").GetString().Should().Be("CONFIRMED");
        }
        counts = await Read(await _client.GetAsync($"/api/classes/{id}/seats"), HttpStatusCode.OK);
        counts.GetProperty("confirmedSeats").GetInt32().Should().Be(1);
        counts.GetProperty("heldSeats").GetInt32().Should().Be(0);
        await Read(await _client.PostAsJsonAsync($"/api/holds/{next}/cancel", new { studentId = Student }), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("Valid", 0)]
    [InlineData("Valid", -1)]
    public async Task CreateClass_RejectsInvalidInput(string? name, int totalSeats)
    {
        await Read(await _client.PostAsJsonAsync("/api/classes", new { name, totalSeats }), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateClass_TrimsName_RejectsOverlongAndDuplicateNamesIncludingRaces()
    {
        await Read(await _client.PostAsJsonAsync("/api/classes", new { name = new string('a', 101), totalSeats = 1 }), HttpStatusCode.BadRequest);
        var name = $"API-{Guid.NewGuid():N}";
        await CreateClass(name: $" {name} ");
        await Read(await _client.PostAsJsonAsync("/api/classes", new { name, totalSeats = 1 }), HttpStatusCode.Conflict);
        var raceName = $"API-{Guid.NewGuid():N}";
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            _client.PostAsJsonAsync("/api/classes", new { name = raceName, totalSeats = 1 })));
        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Created))
            _classes.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("classId").GetGuid());
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(7);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567890123456a")]
    [InlineData("１２３４５６７８９０１２３４５６７")]
    public async Task HoldMutations_RejectInvalidStudentIds(string? studentId)
    {
        var id = Guid.NewGuid();
        await Read(await _client.PostAsJsonAsync("/api/holds", new { classId = id, studentId }), HttpStatusCode.BadRequest);
        foreach (var operation in new[] { "confirm", "cancel" })
            await Read(await _client.PostAsJsonAsync($"/api/holds/{id}/{operation}", new { studentId }), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Errors_MapMissingOwnershipDuplicateAndFullToHttpStatuses()
    {
        var missing = Guid.NewGuid();
        foreach (var path in new[] { $"/api/classes/{missing}/seats", $"/api/holds/{missing}" })
            await Read(await _client.GetAsync(path), HttpStatusCode.NotFound);
        await Read(await _client.PostAsJsonAsync("/api/holds", new { classId = missing, studentId = Student }), HttpStatusCode.NotFound);
        foreach (var operation in new[] { "confirm", "cancel" })
            await Read(await _client.PostAsJsonAsync($"/api/holds/{missing}/{operation}", new { studentId = Student }), HttpStatusCode.NotFound);
        var id = await CreateClass();
        var hold = await Hold(id);
        foreach (var student in new[] { Student, Other })
            await Read(await _client.PostAsJsonAsync("/api/holds", new { classId = id, studentId = student }), HttpStatusCode.Conflict);
        foreach (var operation in new[] { "confirm", "cancel" })
            await Read(await _client.PostAsJsonAsync($"/api/holds/{hold}/{operation}", new { studentId = Other }), HttpStatusCode.Forbidden);
        await Read(await _client.GetAsync("/api/students/123/holds"), HttpStatusCode.BadRequest);
        await Read(await _client.PostAsync("/api/holds", new StringContent("{}", Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ClockExpiredHolds_AreReportedExpiredAndReleaseCapacityBeforeSweep()
    {
        var id = await CreateClass();
        var hold = await Hold(id);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=postgres;Database=school_admission;Username=postgres;Password=postgres").Options;
        await using var db = new ApplicationDbContext(options);
        await db.AdmissionHolds.Where(h => h.Id == hold)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
        var status = await Read(await _client.GetAsync($"/api/holds/{hold}"), HttpStatusCode.OK);
        status.GetProperty("status").GetString().Should().Be("EXPIRED");
        var history = await Read(await _client.GetAsync($"/api/students/{Student}/holds"), HttpStatusCode.OK);
        history.EnumerateArray().Single(h => h.GetProperty("holdId").GetGuid() == hold)
            .GetProperty("status").GetString().Should().Be("EXPIRED");
        var classes = await Read(await _client.GetAsync("/api/classes"), HttpStatusCode.OK);
        classes.EnumerateArray().Single(c => c.GetProperty("classId").GetGuid() == id)
            .GetProperty("availableSeats").GetInt32().Should().Be(1);
        await Read(await _client.PostAsJsonAsync($"/api/holds/{hold}/confirm", new { studentId = Student }), HttpStatusCode.Conflict);
        await Hold(id);
    }
}
