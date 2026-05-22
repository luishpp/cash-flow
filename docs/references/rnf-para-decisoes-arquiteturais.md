# RNFs → Decisões Arquiteturais

Guia prático para traduzir Requisitos Não-Funcionais em decisões de arquitetura concretas no ecossistema .NET / Azure.

**Estrutura de cada entrada:**
- **RNF** — o requisito conforme declarado pelo negócio ou time técnico
- **Decisão Arquitetural** — o que muda na arquitetura para atender o RNF
- **Implementação** — padrões, componentes e código de referência
- **Trade-off** — o que se ganha e o que se paga

---

## 1. Disponibilidade

### RNF 1.1
> "O sistema de pagamentos deve ter disponibilidade de 99,95% (≤ 4,38h de downtime/ano)."

**Decisão Arquitetural:**
Deploy multi-região ativo/passivo com failover automático via Azure Front Door.
Banco de dados com geo-replicação e failover group.

**Implementação:**
- Azure Front Door com health probes apontando para `/health` de cada região
- Azure SQL Failover Group entre Brazil South e East US
- ASP.NET Health Checks com checagem de dependências críticas:

```csharp
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString, name: "sqldb", tags: new[] { "ready" })
    .AddAzureServiceBusQueue(sbConnection, "pagamentos", name: "servicebus")
    .AddRedis(redisConnection, name: "cache");
```

- Kubernetes liveness e readiness probes mapeados para os endpoints de health

**Trade-off:**
Custo de infraestrutura duplicada (~1,6x a 2x) + complexidade de dados replicados.
Ganho: downtime medido em minutos, não horas.

---

### RNF 1.2
> "Deploys não podem causar downtime perceptível ao usuário."

**Decisão Arquitetural:**
Zero-downtime deployment com rolling update e readiness gates.

**Implementação:**
- AKS com `RollingUpdate` strategy (`maxUnavailable: 0`, `maxSurge: 1`)
- Readiness probe aguardando warmup da aplicação
- Azure DevOps pipeline com slot swap no App Service (staging → production)
- Database migrations retrocompatíveis (expand-contract pattern)

**Trade-off:**
Migrations ficam mais complexas (sempre aditivas, nunca destrutivas em um único deploy).
Ganho: releases a qualquer hora, sem janela de manutenção.

---

## 2. Escalabilidade

### RNF 2.1
> "O sistema deve suportar picos de 10x a carga normal durante Black Friday sem degradação."

**Decisão Arquitetural:**
Arquitetura stateless com autoscaling reativo e preditivo.
Desacoplamento de operações pesadas via filas.

**Implementação:**
- App Service ou AKS com autoscale baseado em CPU + custom metric (requests/queue depth)
- KEDA (Kubernetes Event-Driven Autoscaling) para escalar workers baseado em Service Bus queue length:

```yaml
triggers:
  - type: azure-servicebus
    metadata:
      queueName: pedidos
      messageCount: "50"
      connectionFromEnv: SB_CONNECTION
```

- Azure Cache for Redis para session state (zero estado no app server)
- Cosmos DB com partition key bem escolhida para distribuição uniforme

**Trade-off:**
Complexidade operacional de autoscale + custo variável difícil de prever.
Ganho: paga-se pelo pico apenas durante o pico.

---

### RNF 2.2
> "O serviço de relatórios não pode impactar a performance das APIs transacionais."

**Decisão Arquitetural:**
CQRS — separação física de leitura e escrita.

**Implementação:**
- Banco transacional (Azure SQL) para comandos (writes)
- Read replica ou projeção em Cosmos DB/Azure SQL Read Replica para queries
- Sincronização via eventos no Service Bus ou Change Data Capture
- APIs de relatório apontando exclusivamente para a réplica de leitura

```
[API Transacional] → Azure SQL (Primary)
                         ↓ (CDC / Events)
                   [Service Bus Topic]
                         ↓
                   [Worker Projeção]
                         ↓
[API Relatórios]  → Azure SQL (Read Replica) / Cosmos DB
```

**Trade-off:**
Eventual consistency nos relatórios (segundos de atraso).
Ganho: queries pesadas nunca bloqueiam transações.

---

## 3. Resiliência

### RNF 3.1
> "Falha no serviço de e-mail não pode impedir a conclusão de um pedido."

**Decisão Arquitetural:**
Comunicação assíncrona com garantia de entrega.
Fallback e retry com dead letter.

**Implementação:**
- Pedido publica evento `PedidoConfirmado` no Azure Service Bus Topic
- Subscriber de e-mail consome de forma independente
- Retry com backoff exponencial via Polly / `Microsoft.Extensions.Resilience`:

```csharp
builder.Services.AddHttpClient("EmailService")
    .AddResilienceHandler("email-pipeline", pipeline =>
    {
        pipeline.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(2)
        });
        pipeline.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(60)
        });
    });
```

- Mensagens que falharam 3x vão para DLQ → alerta no Azure Monitor → reprocessamento manual ou automático

**Trade-off:**
E-mail pode atrasar minutos em cenário de falha.
Ganho: pedido NUNCA falha por causa de e-mail.

---

### RNF 3.2
> "O sistema deve continuar operando em modo degradado se o serviço de recomendação ficar indisponível."

**Decisão Arquitetural:**
Graceful degradation com feature flag e fallback estático.

**Implementação:**
- Azure App Configuration + Feature Management para controle de features
- Fallback para "produtos mais vendidos" (cache local) quando recomendação está offline

```csharp
if (await _featureManager.IsEnabledAsync("RecomendacaoAtiva"))
{
    try
    {
        return await _recomendacaoService.ObterAsync(userId);
    }
    catch (BrokenCircuitException)
    {
        _logger.LogWarning("Recomendação indisponível, usando fallback");
    }
}
return await _fallbackCache.ObterMaisVendidosAsync();
```

**Trade-off:**
Experiência menos personalizada durante degradação.
Ganho: página sempre carrega, conversão não cai a zero.

---

## 4. Observabilidade

### RNF 4.1
> "Toda requisição deve ser rastreável de ponta a ponta entre todos os serviços."

**Decisão Arquitetural:**
Distributed tracing com OpenTelemetry e correlation ID propagado automaticamente.

**Implementação:**
- OpenTelemetry SDK para .NET com exporter para Application Insights:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSqlClientInstrumentation(o => o.SetDbStatementForText = true)
            .AddSource("Pedidos.API")
            .AddAzureMonitorTraceExporter(o =>
                o.ConnectionString = appInsightsConnectionString);
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddAzureMonitorMetricExporter(o =>
                o.ConnectionString = appInsightsConnectionString);
    });
```

- W3C TraceContext propagado automaticamente entre HTTP calls
- Service Bus: propagação manual do `Activity` no message header
- Application Insights Application Map para visualizar dependências

**Trade-off:**
Overhead marginal por requisição (~1-2ms) + custo de ingestão de telemetria.
Ganho: tempo de diagnóstico de incidentes cai de horas para minutos.

---

### RNF 4.2
> "Time de negócio precisa de alertas em tempo real quando a taxa de conversão cair abaixo de 2%."

**Decisão Arquitetural:**
Métricas de negócio como cidadãos de primeira classe na telemetria.

**Implementação:**
- Custom metric via `System.Diagnostics.Metrics`:

```csharp
public class ConversaoMetrics
{
    private readonly Counter<long> _pedidosCriados;
    private readonly Counter<long> _checkoutsIniciados;

    public ConversaoMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Ecommerce.Conversao");
        _pedidosCriados = meter.CreateCounter<long>("pedidos.criados");
        _checkoutsIniciados = meter.CreateCounter<long>("checkouts.iniciados");
    }

    public void RegistrarCheckout() => _checkoutsIniciados.Add(1);
    public void RegistrarPedido() => _pedidosCriados.Add(1);
}
```

- Azure Monitor Alert Rule com KQL:

```kql
customMetrics
| where name in ("checkouts.iniciados", "pedidos.criados")
| summarize checkouts = sumif(valueSum, name == "checkouts.iniciados"),
            pedidos  = sumif(valueSum, name == "pedidos.criados")
            by bin(timestamp, 15m)
| extend taxa_conversao = round(todouble(pedidos) / todouble(checkouts) * 100, 2)
| where taxa_conversao < 2.0
```

- Action Group → Teams channel + PagerDuty

**Trade-off:**
Requer disciplina para instrumentar novos fluxos de negócio.
Ganho: negócio detecta problema antes do suporte receber reclamações.

---

## 5. Segurança

### RNF 5.1
> "Nenhum segredo pode existir em código-fonte, variáveis de ambiente ou arquivos de configuração."

**Decisão Arquitetural:**
Centralização de segredos no Azure Key Vault com Managed Identity.

**Implementação:**
- Managed Identity atribuída ao App Service / AKS Pod
- Key Vault referenciado como configuration provider:

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://meu-vault.vault.azure.net/"),
    new DefaultAzureCredential());
```

- Referências no App Service:
  `@Microsoft.KeyVault(VaultName=meu-vault;SecretName=SqlPassword)`
- Azure Policy para bloquear deploy de recursos sem Managed Identity
- Secret rotation automática via Key Vault rotation policy

**Trade-off:**
Latência na primeira leitura de secrets (~50-200ms) + dependência do Key Vault.
Ganho: zero segredos hardcoded, auditoria completa de acesso, rotação sem redeploy.

---

### RNF 5.2
> "APIs expostas devem aceitar apenas tokens válidos com escopo correto."

**Decisão Arquitetural:**
OAuth 2.0 / OIDC com Microsoft Entra ID + validação de escopo por endpoint.

**Implementação:**

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("LerPedidos", policy =>
        policy.RequireScope("Pedidos.Read"))
    .AddPolicy("CriarPedidos", policy =>
        policy.RequireScope("Pedidos.Write")
              .RequireRole("OperadorPedidos"));
```

```csharp
[Authorize(Policy = "CriarPedidos")]
[HttpPost]
public async Task<IActionResult> CriarPedido([FromBody] CriarPedidoCommand cmd)
```

- Azure API Management na frente para rate limiting, IP filtering e validação de JWT adicional

**Trade-off:**
Complexidade de gestão de app registrations e escopos no Entra ID.
Ganho: controle granular, token revocável, auditoria de quem acessa o quê.

---

## 6. Performance

### RNF 6.1
> "A API de catálogo deve responder em menos de 200ms no p95."

**Decisão Arquitetural:**
Cache distribuído com invalidação orientada a eventos.

**Implementação:**
- Azure Cache for Redis como cache de leitura
- Cache-aside pattern com invalidação via Service Bus:

```csharp
public async Task<Produto?> ObterProdutoAsync(string id)
{
    var cacheKey = $"produto:{id}";
    var cached = await _redis.GetStringAsync(cacheKey);

    if (cached is not null)
        return JsonSerializer.Deserialize<Produto>(cached);

    var produto = await _dbContext.Produtos
        .AsNoTracking()
        .FirstOrDefaultAsync(p => p.Id == id);

    if (produto is not null)
    {
        await _redis.SetStringAsync(cacheKey,
            JsonSerializer.Serialize(produto),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            });
    }

    return produto;
}
```

- Quando produto é atualizado → evento `ProdutoAlterado` → subscriber invalida cache
- Response compression com Brotli no ASP.NET Core
- Output caching para endpoints que retornam dados estáveis

**Trade-off:**
Complexidade de invalidação + custo do Redis.
Ganho: p95 cai de 400-800ms (DB direto) para 20-50ms (cache hit).

---

### RNF 6.2
> "Processamento de arquivo de conciliação de 500k registros deve completar em menos de 5 minutos."

**Decisão Arquitetural:**
Processamento paralelo em batch com streaming de I/O.

**Implementação:**
- Azure Functions com Durable Functions (fan-out/fan-in) ou worker dedicado
- Leitura streaming com `IAsyncEnumerable` para evitar carregar tudo em memória:

```csharp
public async Task ProcessarConciliacaoAsync(Stream arquivo)
{
    var batches = LerRegistrosAsync(arquivo).Chunk(1000);

    await Parallel.ForEachAsync(batches,
        new ParallelOptions { MaxDegreeOfParallelism = 8 },
        async (batch, ct) =>
        {
            await _conciliador.ProcessarBatchAsync(batch, ct);
        });
}

private async IAsyncEnumerable<RegistroConciliacao> LerRegistrosAsync(Stream stream)
{
    using var reader = new StreamReader(stream);
    await foreach (var line in reader.ReadAllLinesAsync())
    {
        yield return ParseRegistro(line);
    }
}
```

- Bulk insert via `SqlBulkCopy` para gravar resultados
- Monitorar GC pressure com `dotnet-counters`

**Trade-off:**
Mais memória durante picos de paralelismo + complexidade de error handling por batch.
Ganho: 500k registros em ~2min vs. 30min+ sequencial.

---

## 7. Manutenibilidade

### RNF 7.1
> "Novos desenvolvedores devem conseguir subir o ambiente e fazer o primeiro deploy em menos de 1 dia."

**Decisão Arquitetural:**
Containerização completa do ambiente de desenvolvimento + IaC.

**Implementação:**
- Dev Container (`.devcontainer/`) com todas as dependências:
  SDK .NET, Azure CLI, SQL Server local, Redis, Service Bus Emulator
- Docker Compose para dependências externas
- Makefile ou `justfile` com comandos padronizados:
  `make setup`, `make test`, `make run`, `make deploy-dev`
- Bicep/Terraform para infraestrutura com ambientes efêmeros por PR
- README.md com "Getting Started" de no máximo 5 passos
- `dotnet new` templates customizados para novos serviços

**Trade-off:**
Investimento inicial de 2-3 dias para montar o setup.
Ganho: onboarding de horas ao invés de dias; "funciona na minha máquina" eliminado.

---

### RNF 7.2
> "A camada de domínio não pode ter dependência direta de infraestrutura."

**Decisão Arquitetural:**
Clean Architecture com dependency inversion estrita, validada por testes arquiteturais.

**Implementação:**
- Estrutura de projetos:

```
src/
├── Pedidos.Domain/          # Entidades, Value Objects, Interfaces
├── Pedidos.Application/     # Use Cases, DTOs, Validators
├── Pedidos.Infrastructure/  # EF Core, Service Bus, Redis
└── Pedidos.API/             # Controllers, Middleware, DI setup
```

- Teste arquitetural com NetArchTest:

```csharp
[Fact]
public void Domain_NaoDeveReferenciar_Infraestrutura()
{
    var result = Types.InAssembly(typeof(Pedido).Assembly)
        .ShouldNot()
        .HaveDependencyOn("Pedidos.Infrastructure")
        .GetResult();

    Assert.True(result.IsSuccessful,
        $"Domain referencia Infrastructure: {string.Join(", ",
            result.FailingTypeNames ?? Array.Empty<string>())}");
}

[Fact]
public void Domain_NaoDeveReferenciar_AspNetCore()
{
    var result = Types.InAssembly(typeof(Pedido).Assembly)
        .ShouldNot()
        .HaveDependencyOnAny("Microsoft.AspNetCore", "Microsoft.Extensions.Http")
        .GetResult();

    Assert.True(result.IsSuccessful);
}
```

- CI pipeline falha se teste arquitetural quebrar

**Trade-off:**
Mais indirection, mais interfaces, mais projetos no solution.
Ganho: domínio testável sem infraestrutura, troca de database/broker sem tocar regras de negócio.

---

## 8. Cenários de Uso

### RNF 8.1
> "O checkout deve funcionar mesmo com 50k usuários simultâneos durante campanhas promocionais."

**Decisão Arquitetural:**
Isolamento do fluxo crítico de checkout com recursos dedicados e priorização.

**Implementação:**
- API de checkout em serviço separado com autoscale independente
- Service Bus queue dedicada com sessions (ordenação por carrinho):

```
[Checkout API]  ──→  [Queue: checkout-commands]  ──→  [Worker Checkout]
     ↑ (dedicado, prioridade alta)                        ↓
[Catálogo API]  ──→  [Queue: catalogo-events]    ──→  [Worker Catálogo]
     ↑ (pode degradar)                                    ↓
```

- Rate limiting diferenciado: checkout tem budget maior que navegação
- Azure Load Testing com perfil simulando Black Friday:

```yaml
# load-test-config.yaml
testPlan: checkout-black-friday.jmx
engineInstances: 5
env:
  - name: RAMP_UP_USERS
    value: "50000"
  - name: RAMP_UP_DURATION
    value: "300"
failureCriteria:
  - avg(response_time_ms) > 500 when request = checkout
  - percentage(error) > 1
```

- Feature flag para desabilitar features não-essenciais sob carga (recomendações, reviews)

**Trade-off:**
Mais infraestrutura dedicada + testes de carga regulares (custo operacional).
Ganho: checkout protegido — o ponto onde dinheiro entra não compete por recursos.

---

### RNF 8.2
> "Importação de catálogo pelo fornecedor (batch diário de ~200k SKUs) não pode impactar a experiência do cliente."

**Decisão Arquitetural:**
Processamento batch isolado com throttling e atualização gradual.

**Implementação:**
- Azure Function com Timer Trigger fora do horário de pico (03:00 UTC)
- Worker dedicado com resource limits separados do pool principal
- Bulk upsert com throttling controlado para não saturar o banco:

```csharp
public async Task ImportarCatalogoAsync(Stream arquivo, CancellationToken ct)
{
    var batches = LerSkusAsync(arquivo).Chunk(500);

    await foreach (var batch in batches.WithCancellation(ct))
    {
        await _repository.UpsertBatchAsync(batch, ct);

        // Throttle: evitar saturar conexões do SQL
        await Task.Delay(TimeSpan.FromMilliseconds(200), ct);

        _metrics.SkusImportados.Add(batch.Length);
    }

    // Invalida cache gradualmente
    await _cacheInvalidator.InvalidarCategoriasPorLoteAsync(ct);
}
```

- Azure SQL: Resource Governor ou Elastic Pool com limites para a connection string de importação
- Monitorar DTU/vCore durante importação via alert rule

**Trade-off:**
Catálogo pode ficar até 24h desatualizado no pior caso.
Ganho: clientes nunca sentem lentidão por causa de importação de fornecedor.

---

## Resumo: Padrão de Raciocínio

Para qualquer RNF, siga este fluxo:

```
1. CLASSIFICAR     → Em qual taxonomia esse RNF se encaixa?
                     (pode ser mais de uma)

2. QUANTIFICAR     → Qual o número concreto?
                     ("alta disponibilidade" não é RNF; "99,95%" é)

3. DECIDIR         → Qual padrão arquitetural atende?
                     (cache, fila, replicação, CQRS, circuit breaker...)

4. IMPLEMENTAR     → Quais componentes Azure/.NET materializam a decisão?

5. VALIDAR         → Como provar que o RNF está sendo atendido?
                     (teste de carga, chaos test, métrica, alerta)

6. DOCUMENTAR      → Qual o trade-off aceito?
                     (custo, complexidade, eventual consistency...)
```

Cada decisão arquitetural é uma aposta: você está trocando complexidade
em um eixo por garantia em outro. Documentar o trade-off é tão
importante quanto a decisão em si.
