using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CashFlow.Bdd.Tests.Setup;
using FluentAssertions;
using Reqnroll;

namespace CashFlow.Bdd.Tests.Steps;

[Binding]
public sealed class AutenticacaoE2ESteps
{
    private HttpResponseMessage? _response;
    private JsonElement? _responseJson;

    // Estado entre passos do MESMO cenário (Reqnroll instancia [Binding] por scenario por default).
    private string? _accessToken;
    private string? _currentRefreshToken;
    private string? _previousRefreshToken;

    private sealed record TokenResponse(
        string AccessToken,
        string TokenType,
        DateTimeOffset ExpiresAtUtc,
        string Role,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc);

    // Pós-ADR-027: cada step pega o cliente correto via roteamento por URL prefix.
    // /api/v1/auth/* → IdentityClient ; demais → TransactionsClient.
    private static HttpClient ClientFor(string url) => CashFlowApiFixture.ClientFor(url);

    // ──────────────────────────────────────────────────────────────────────────
    // GIVEN
    // ──────────────────────────────────────────────────────────────────────────

    [Given("que estou autenticado com credenciais demo válidas")]
    public async Task DadoAutenticadoComCredenciaisDemo()
    {
        var url = "/api/v1/auth/login";
        var resp = await ClientFor(url).PostAsJsonAsync(url, new
        {
            username = CashFlowApiFixture.DemoUsername,
            password = CashFlowApiFixture.DemoPassword
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        token.Should().NotBeNull();
        _accessToken = token!.AccessToken;
        _currentRefreshToken = token.RefreshToken;
    }

    [Given("rotaciono o refresh token uma vez")]
    public async Task DadoRotacionoRefresh()
    {
        _currentRefreshToken.Should().NotBeNullOrWhiteSpace("scenario depende do passo Dado anterior");

        var url = "/api/v1/auth/refresh";
        var resp = await ClientFor(url).PostAsJsonAsync(url,
            new { refreshToken = _currentRefreshToken });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        _previousRefreshToken = _currentRefreshToken;
        _currentRefreshToken = token!.RefreshToken;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // WHEN
    // ──────────────────────────────────────────────────────────────────────────

    [When("faço POST em {string} com credenciais demo válidas")]
    public async Task QuandoLoginCredenciaisDemo(string url)
    {
        _response = await ClientFor(url).PostAsJsonAsync(url, new
        {
            username = CashFlowApiFixture.DemoUsername,
            password = CashFlowApiFixture.DemoPassword
        });
        await CaptureJsonAsync();
    }

    [When("faço POST em {string} com username {string} e password {string}")]
    public async Task QuandoLoginCustom(string url, string username, string password)
    {
        _response = await ClientFor(url).PostAsJsonAsync(url, new { username, password });
    }

    [When("faço {int} tentativas de login com password {string}")]
    public async Task QuandoNTentativasComSenhaErrada(int n, string password)
    {
        var url = "/api/v1/auth/login";
        for (var i = 0; i < n; i++)
        {
            _response = await ClientFor(url).PostAsJsonAsync(url, new
            {
                username = CashFlowApiFixture.DemoUsername,
                password
            });
            ((int)_response.StatusCode).Should().Be(401,
                $"tentativa #{i + 1} de {n} deve falhar com 401");
        }
    }

    [When("faço POST em {string} sem token com payload de crédito de {decimal}")]
    public async Task QuandoPostTransacaoSemToken(string url, decimal valor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(BuildTransactionPayload(valor))
        };
        _response = await ClientFor(url).SendAsync(request);
    }

    [When("faço POST em {string} com token e payload de crédito de {decimal}")]
    public async Task QuandoPostTransacaoComToken(string url, decimal valor)
    {
        _accessToken.Should().NotBeNullOrWhiteSpace("o passo Dado deveria ter autenticado");

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(BuildTransactionPayload(valor))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        _response = await ClientFor(url).SendAsync(request);
        await CaptureJsonAsync();
    }

    [When("faço POST em {string} com o refresh token atual")]
    public async Task QuandoPostComRefreshAtual(string url)
    {
        _currentRefreshToken.Should().NotBeNullOrWhiteSpace();
        _response = await ClientFor(url).PostAsJsonAsync(url, new { refreshToken = _currentRefreshToken });
        await CaptureJsonAsync();
    }

    [When("faço POST em {string} com o refresh token anterior")]
    public async Task QuandoPostComRefreshAnterior(string url)
    {
        _previousRefreshToken.Should().NotBeNullOrWhiteSpace(
            "o cenário precisa ter rotacionado o token antes");
        _response = await ClientFor(url).PostAsJsonAsync(url, new { refreshToken = _previousRefreshToken });
        await CaptureJsonAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // THEN
    // ──────────────────────────────────────────────────────────────────────────

    [Then("a resposta deve ser {int}")]
    public void EntaoStatus(int expected)
    {
        _response.Should().NotBeNull();
        ((int)_response!.StatusCode).Should().Be(expected,
            "body recebido: {0}", _response.Content.ReadAsStringAsync().Result);
    }

    [Then("a resposta deve conter um access token não vazio")]
    public void EntaoResponseTemAccessToken()
    {
        _responseJson.Should().NotBeNull();
        _responseJson!.Value.TryGetProperty("accessToken", out var token).Should().BeTrue();
        var raw = token.GetString();
        raw.Should().NotBeNullOrWhiteSpace();

        // Captura para os passos seguintes do mesmo cenário.
        _accessToken = raw;
    }

    [Then("a resposta deve conter um refresh token não vazio")]
    public void EntaoResponseTemRefreshToken()
    {
        _responseJson.Should().NotBeNull();
        _responseJson!.Value.TryGetProperty("refreshToken", out var token).Should().BeTrue();
        var raw = token.GetString();
        raw.Should().NotBeNullOrWhiteSpace();

        _previousRefreshToken = _currentRefreshToken;
        _currentRefreshToken = raw;
    }

    [Then("o novo refresh token deve ser diferente do anterior")]
    public void EntaoNovoRefreshDiferenteAnterior()
    {
        _currentRefreshToken.Should().NotBeNullOrWhiteSpace();
        _previousRefreshToken.Should().NotBeNullOrWhiteSpace();
        _currentRefreshToken.Should().NotBe(_previousRefreshToken);
    }

    [Then("a role retornada deve ser {string}")]
    public void EntaoRoleRetornada(string expected)
    {
        _responseJson.Should().NotBeNull();
        _responseJson!.Value.TryGetProperty("role", out var role).Should().BeTrue();
        role.GetString().Should().Be(expected);
    }

    [Then("o body deve conter um id de transação válido")]
    public void EntaoIdDeTransacaoValido()
    {
        _responseJson.Should().NotBeNull();
        // POST /transactions sempre retorna array (batch transacional — ADR-025).
        _responseJson!.Value.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
        _responseJson.Value.GetArrayLength().Should().BeGreaterThan(0);
        var first = _responseJson.Value[0];
        first.TryGetProperty("id", out var id).Should().BeTrue();
        Guid.TryParse(id.GetString(), out var parsed).Should().BeTrue();
        parsed.Should().NotBe(Guid.Empty);
    }

    // POST /transactions é sempre batch (array) — wrap único item.
    private static object[] BuildTransactionPayload(decimal valor) =>
    [
        new
        {
            type = "credit",
            amount = valor,
            description = "BDD E2E payload",
            movementDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        }
    ];

    private async Task CaptureJsonAsync()
    {
        _responseJson = null;
        if (_response is null) return;
        var contentType = _response.Content.Headers.ContentType?.MediaType;
        if (contentType != "application/json") return;
        var raw = await _response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw)) return;
        _responseJson = JsonDocument.Parse(raw).RootElement;
    }
}
