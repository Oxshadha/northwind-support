using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.IntegrationTests;

public sealed class AuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Sara = "sara.lindqvist@northwind-support.example";
    private const string Yuki = "yuki.tanaka@northwind-support.example";

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpResponseMessage> Login(string email, string password) =>
        await _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task<string> TokenFor(string email)
    {
        var response = await Login(email, SupportDbSeeder.DevelopmentPassword);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task AProtectedEndpoint_Is401WithoutAToken_And200WithOne()
    {
        var anonymous = await Send(HttpMethod.Get, "/api/tickets");
        var signedIn = await Send(HttpMethod.Get, "/api/tickets", await TokenFor(Sara));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("application/json; charset=utf-8", anonymous.Content.Headers.ContentType?.ToString());
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task EveryEndpoint_RequiresSignIn_ExceptSignInAndHealth()
    {
        // Enforced centrally: walk every routed endpoint the app exposes and call it anonymously.
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.NotEmpty(endpoints);

        foreach (var endpoint in endpoints)
        {
            var url = "/" + endpoint.RoutePattern.RawText!.TrimStart('/').Replace("{id:int}", "1", StringComparison.Ordinal);
            // Endpoints mapped for any method (the health check) carry no method metadata.
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single() ?? "GET";
            var anonymousAllowed = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

            var response = await Send(new HttpMethod(method), url, body: method is "GET" ? null : new { });

            if (anonymousAllowed)
            {
                Assert.Contains(url, new[] { "/api/auth/login", "/health" });
            }
            else
            {
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {url} answered {response.StatusCode} without a token.");
            }
        }
    }

    [Fact]
    public async Task AWrongPassword_Is401_WithNoToken_AndTheSameAnswerAsAnUnknownEmail()
    {
        var wrongPassword = await Login(Sara, "not-the-password");
        var unknownEmail = await Login("nobody@northwind-support.example", "not-the-password");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        var wrongBody = await wrongPassword.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", wrongBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Detail(wrongBody), Detail(await unknownEmail.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task AnInactiveAgent_CannotSignIn_WithTheRightPassword()
    {
        var response = await Login(Yuki, SupportDbSeeder.DevelopmentPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_Is401WithoutAToken_AndNamesTheAgentWithOne()
    {
        var anonymous = await Send(HttpMethod.Get, "/api/auth/me");
        var signedIn = await Send(HttpMethod.Get, "/api/auth/me", await TokenFor(Sara));

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var body = JsonDocument.Parse(await signedIn.Content.ReadAsStringAsync());
        Assert.Equal("Sara Lindqvist", body.RootElement.GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task ATamperedToken_IsRejected()
    {
        var token = await TokenFor(Sara);
        var parts = token.Split('.');
        // Swap the payload for one claiming to be another agent, keeping the old signature.
        var forged = $"{parts[0]}.{Base64Url("{\"sub\":\"1\",\"name\":\"Alex Turner\"}")}.{parts[2]}";

        var response = await Send(HttpMethod.Get, "/api/auth/me", forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_IsOpen_AndReportsTheDatabase()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.RootElement.GetProperty("checks").GetProperty("database").GetString());
    }

    [Fact]
    public async Task Swagger_StaysOpen()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WhoEscalated_ComesFromTheToken_NotFromTheRequestBody()
    {
        var token = await TokenFor(Sara);

        var escalate = await Send(
            HttpMethod.Post, "/api/tickets/3/escalate", token,
            new { reason = "Customer needs the seats before Monday", escalatedBy = "Mallory" });
        escalate.EnsureSuccessStatusCode();

        using var history = JsonDocument.Parse(await (await Send(HttpMethod.Get, "/api/tickets/3/escalations", token)).Content.ReadAsStringAsync());
        Assert.Equal("Sara Lindqvist", history.RootElement[0].GetProperty("escalatedBy").GetString());
    }

    private static string? Detail(string problemJson)
    {
        using var problem = JsonDocument.Parse(problemJson);
        return problem.RootElement.GetProperty("detail").GetString();
    }

    private static string Base64Url(string json) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
