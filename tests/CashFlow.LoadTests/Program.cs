using System.Net.Http.Json;
using NBomber.CSharp;
using NBomber.Http.CSharp;

namespace CashFlow.LoadTests;

/// <summary>
/// Validação empírica do RNF-02:
///   "Em dias de picos, o serviço de consolidado diário recebe 50 requisições por segundo,
///    com no máximo 5% de perda de requisições."
///
/// Executa GET /api/v1/balance/{date} contra a Balance API rodando em http://localhost:5002
/// por 60s a 50 req/s (3.000 requisições no total). Sucesso = HTTP 200 + ≥95% de aprovação.
///
/// Pré-requisitos:
///   1. docker compose up --build -d
///   2. Aguardar healthchecks (~10s)
///   3. POST /api/v1/auth/login na Transactions API (5001) para obter o JWT bearer
///   4. Exportar: $env:CASHFLOW_TOKEN = "eyJ..." (PowerShell) ou export CASHFLOW_TOKEN=... (bash)
///   5. dotnet run --project tests/CashFlow.LoadTests --configuration Release
/// </summary>
public static class Program
{
    private const string BalanceApiBaseUrl = "http://localhost:5002";
    private const string TransactionsApiBaseUrl = "http://localhost:5001";
    private const int TargetRatePerSecond = 50;
    private const int RampUpSeconds = 10;
    private const int SustainSeconds = 60;
    private const double MinAcceptablePassRate = 0.95; // 5% perda máx, conforme RNF-02

    public static async Task<int> Main()
    {
        var token = Environment.GetEnvironmentVariable("CASHFLOW_TOKEN")
                    ?? await TryAutoLoginAsync();

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine(
                "ERRO: CASHFLOW_TOKEN não definido e auto-login falhou. " +
                "Suba os serviços (docker compose up) e/ou faça login manualmente em " +
                $"{TransactionsApiBaseUrl}/api/v1/auth/login.");
            return 1;
        }

        var date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var scenario = Scenario.Create("balance_query_50rps", async ctx =>
        {
            var request = Http.CreateRequest("GET", $"{BalanceApiBaseUrl}/api/v1/balance/{date}")
                .WithHeader("Authorization", $"Bearer {token}");

            var response = await Http.Send(httpClient, request);
            return response;
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // Ramp-up: 0 → 50 rps em 10s (evita falso-positivo de cold start).
            // RampingInject (open model) — incrementa a taxa de injeção; precisa casar com Inject abaixo
            // (NBomber proíbe misturar open model com closed model como RampingConstant/KeepConstant).
            Simulation.RampingInject(rate: TargetRatePerSecond,
                                     interval: TimeSpan.FromSeconds(1),
                                     during: TimeSpan.FromSeconds(RampUpSeconds)),
            // Sustenta 50 rps por 60s — 3.000 requisições alvo
            Simulation.Inject(rate: TargetRatePerSecond,
                              interval: TimeSpan.FromSeconds(1),
                              during: TimeSpan.FromSeconds(SustainSeconds)));

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder("load-test-reports")
            .Run();

        // Avaliação do critério RNF-02
        var scenarioStats = stats.ScenarioStats[0];
        var totalRequests = scenarioStats.AllRequestCount;
        var okRequests = scenarioStats.Ok.Request.Count;
        var failRequests = scenarioStats.Fail.Request.Count;
        var passRate = totalRequests == 0 ? 0 : (double)okRequests / totalRequests;

        Console.WriteLine();
        Console.WriteLine("===== RNF-02 verification =====");
        Console.WriteLine($"  Target rate:      {TargetRatePerSecond} req/s sustained for {SustainSeconds}s");
        Console.WriteLine($"  Total requests:   {totalRequests}");
        Console.WriteLine($"  OK:               {okRequests}");
        Console.WriteLine($"  Failed:           {failRequests}");
        Console.WriteLine($"  Pass rate:        {passRate:P2}");
        Console.WriteLine($"  Min acceptable:   {MinAcceptablePassRate:P0}");
        Console.WriteLine($"  Result:           {(passRate >= MinAcceptablePassRate ? "PASS ✅" : "FAIL ❌")}");
        Console.WriteLine();

        return passRate >= MinAcceptablePassRate ? 0 : 1;
    }

    /// <summary>
    /// Tenta login com as credenciais demo de appsettings (carlos / S3cret!ChangeMe).
    /// Se falhar (stack não está de pé, credenciais mudaram), retorna null.
    /// </summary>
    private static async Task<string?> TryAutoLoginAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await http.PostAsJsonAsync(
                $"{TransactionsApiBaseUrl}/api/v1/auth/login",
                new { username = "carlos", password = "S3cret!ChangeMe" });

            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
            return body?.AccessToken;
        }
        catch
        {
            return null;
        }
    }

    private sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, string Role);
}
