# Dicionário Técnico — Testes, Métricas, Segurança & Observabilidade

---

## 1. FRAMEWORKS DE TESTE

---

### xUnit

**Descrição:** Framework de testes unitários para .NET criado pelos mesmos autores do NUnit v2 (James Newkirk, Brad Wilson). Projetado com princípios modernos: sem herança obrigatória, sem `[SetUp]`/`[TearDown]` — usa constructor/`IDisposable` para lifecycle. Cada teste roda em uma instância nova da classe (isolation by design). É o framework padrão de fato para projetos .NET modernos e o usado internamente pelo time do ASP.NET Core.

**Onde usar:** Projetos .NET 6+ greenfield; bibliotecas open-source; qualquer cenário onde isolation forte entre testes é desejável; projetos que seguem o ecossistema Microsoft moderno.

**Prós:** Isolation real (instância por teste elimina estado compartilhado acidental); modelo extensível via `IClassFixture<T>` e `ICollectionFixture<T>` para shared context controlado; suporte nativo a teorias (`[Theory]` + `[InlineData]`/`[MemberData]`/`[ClassData]`) para testes parametrizados; paralelismo habilitado por padrão (collection-level); integração excelente com `dotnet test` e CI/CD.

**Contras:** Curva para quem vem do NUnit (não tem `[SetUp]`); paralelismo default pode quebrar testes que compartilham recursos (banco, arquivo) se não forem isolados em collections; `ITestOutputHelper` para log em vez de `Console.WriteLine` (confunde iniciantes); menos atributos "prontos" comparado ao NUnit (ex: `[Retry]`, `[Timeout]` nativos).

**Exemplo .NET:**
```csharp
public class PedidoServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _db;
    public PedidoServiceTests(DatabaseFixture db) => _db = db;

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CriarPedido_ValorInvalido_DeveLancarException(decimal valor)
    {
        var service = new PedidoService(_db.Context);
        Assert.Throws<DomainException>(() => service.Criar(valor));
    }
}
```

---

### NUnit

**Descrição:** Framework de testes mais antigo e maduro do ecossistema .NET, inspirado no JUnit. Usa modelo baseado em atributos (`[SetUp]`, `[TearDown]`, `[TestFixture]`). Uma única instância da classe de teste é reutilizada entre os testes (compartilha estado via setup/teardown). Rico em atributos de controle: retry, timeout, order, platform, culture, parallelizable.

**Onde usar:** Projetos legados que já o utilizam; cenários que precisam de atributos avançados nativos (`[Retry]`, `[Timeout]`, `[Order]`, `[Platform]`); equipes habituadas ao modelo SetUp/TearDown; migração gradual de codebases antigas.

**Prós:** Constraint model poderoso (`Assert.That(x, Is.EqualTo(y).Within(0.01))`); enorme variedade de atributos built-in; comunidade grande e documentação madura; `[TestCaseSource]` e `[ValueSource]` para parametrização rica; suporte a paralelismo configurável por fixture ou método.

**Contras:** Instância compartilhada entre testes pode causar side effects acidentais; modelo SetUp/TearDown pode obscurecer dependências do teste (menos explícito que constructor injection); paralelismo desligado por default (precisa de `[Parallelizable]`); mais verboso para DI comparado ao xUnit.

---

### MSTest

**Descrição:** Framework oficial da Microsoft, presente desde o Visual Studio 2005. Passou por renovação significativa na v2/v3 (MSTest SDK) com suporte a paralelismo, extensibilidade e source generators. Usa `[TestClass]`/`[TestMethod]` e `[TestInitialize]`/`[TestCleanup]`.

**Onde usar:** Organizações com governance Microsoft-first; projetos que já o adotam e não justificam migração; integração nativa com Visual Studio (templates e tooling); equipes enterprise com padronização corporativa.

**Prós:** Integração nativa com Visual Studio e Azure DevOps; MSTest v3 com melhorias significativas (source generators, paralelismo, extensibilidade); `[DataRow]` e `[DynamicData]` para testes parametrizados; suporte oficial e LTS da Microsoft; menor curva de aprendizado para quem já usa o ecossistema VS.

**Contras:** Historicamente mais lento e menos flexível que xUnit/NUnit; comunidade menor de contribuições open-source; menos extensibilidade que xUnit (embora v3 tenha melhorado); `[AssemblyInitialize]` requer método estático (design rígido).

---

### Comparativo Rápido: xUnit vs NUnit vs MSTest

| Aspecto | xUnit | NUnit | MSTest |
|---|---|---|---|
| Instância por teste | Sim (nova a cada teste) | Não (reutiliza) | Não (reutiliza) |
| Paralelismo default | Sim (collection-level) | Não (opt-in) | Não (opt-in) |
| Setup/Teardown | Constructor / IDisposable | [SetUp] / [TearDown] | [TestInitialize] / [TestCleanup] |
| Parametrização | [Theory] + InlineData/MemberData | [TestCase] + TestCaseSource | [DataRow] + DynamicData |
| Assertion style | Assert.Equal, Assert.Throws | Assert.That (constraint model) | Assert.AreEqual |
| Recomendação atual | Greenfield .NET moderno | Legado ou atributos avançados | Enterprise Microsoft-first |

---

## 2. TIPOS E ESTRATÉGIAS DE TESTE

---

### Teste Unitário

**Descrição:** Testa uma unidade isolada de código (método, classe) sem dependências externas. Dependências são substituídas por mocks/stubs (Moq, NSubstitute, FakeItEasy). Deve ser rápido (< 10ms), determinístico e sem I/O.

**Onde usar:** Regras de negócio em domain layer; validações; cálculos; mapeamentos; qualquer lógica pura.

**Prós:** Feedback instantâneo; localiza defeitos com precisão; executa milhares em segundos; documenta o comportamento esperado.

**Contras:** Não detecta problemas de integração; excesso de mocking pode criar testes frágeis que testam a implementação e não o comportamento; falsa sensação de segurança se só testar caminhos felizes.

---

### Teste de Integração

**Descrição:** Valida a interação entre componentes reais — banco de dados, APIs externas, message brokers, file system. No ASP.NET Core, usa-se `WebApplicationFactory<T>` para testar a pipeline HTTP completa in-memory. Testcontainers permite subir dependências reais via Docker.

**Onde usar:** Repositories com queries complexas; controllers/endpoints; integração com serviços externos; fluxos que cruzam camadas.

**Prós:** Detecta problemas reais de integração (queries, serialização, middleware); `WebApplicationFactory` permite testar o pipeline real sem deploy; maior confiança que unitários isolados.

**Contras:** Mais lento (I/O real); setup mais complexo (banco, containers); testes podem ser flaky por dependências externas; custo de manutenção maior.

**Exemplo .NET:**
```csharp
public class PedidoEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public PedidoEndpointTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task Post_PedidoValido_Retorna201()
    {
        var response = await _client.PostAsJsonAsync("/api/pedidos", new { Valor = 100m });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
```

---

### Teste E2E (End-to-End)

**Descrição:** Valida o fluxo completo do sistema do ponto de vista do usuário final — UI, API, banco, serviços externos, tudo conectado. Ferramentas: Playwright (.NET), Selenium, Cypress. Simula interação real do usuário.

**Onde usar:** Fluxos críticos de negócio (checkout, login, onboarding); smoke tests pós-deploy; validação de ambientes de staging.

**Prós:** Maior nível de confiança possível; detecta problemas de integração entre todos os componentes; valida a experiência real do usuário.

**Contras:** Extremamente lento; frágil (qualquer mudança de UI pode quebrar); setup e manutenção caros; difícil de debugar; flaky por natureza.

---

### Teste de Contrato (Contract Testing)

**Descrição:** Garante que o contrato entre consumer e provider de uma API/mensagem não quebre. O consumer define suas expectativas (contrato); o provider valida que as atende. Ferramenta principal: Pact.

**Onde usar:** Microsserviços com múltiplos consumers; APIs públicas versionadas; event-driven architectures (schema de eventos).

**Prós:** Detecta breaking changes antes do deploy; permite times independentes evoluírem seus serviços; mais rápido que E2E para validar integrações.

**Contras:** Requer setup de Pact Broker; ambos os lados precisam adotar; não substitui testes de integração completos.

---

### Teste de Carga / Performance

**Descrição:** Avalia o comportamento do sistema sob volume esperado (load test), volume extremo (stress test) e volume sustentado (soak test). Ferramentas: k6 (Grafana), NBomber (.NET nativo), JMeter, Azure Load Testing.

**Onde usar:** Pré-produção de sistemas críticos; validação de SLAs; capacity planning; identificação de bottlenecks.

**Prós:** Identifica limites reais do sistema; valida SLAs e SLOs; detecta memory leaks e degradação sob carga.

**Contras:** Requer ambiente representativo (infra similar a produção); resultados podem variar; complexidade de setup; custo de infra para gerar carga.

---

### Teste de Mutação (Mutation Testing)

**Descrição:** Avalia a qualidade dos testes introduzindo mutações no código (troca `>` por `<`, remove linhas, inverte condições) e verifica se os testes detectam. Se o teste continua passando com a mutação, o teste é fraco. Ferramenta .NET: Stryker.NET.

**Onde usar:** Avaliar cobertura real (não só line coverage); domínios críticos onde a qualidade dos testes é vital; code reviews de suítes de teste.

**Prós:** Métrica muito mais real que code coverage; identifica testes que passam por coincidência; melhora a assertividade do suite.

**Contras:** Extremamente lento (roda N variações do build); pode gerar falsos positivos (mutações equivalentes); consome muito recurso de CI.

---

### Pirâmide de Testes

**Descrição:** Modelo que define a proporção ideal entre tipos de teste: base larga de unitários (rápidos, baratos), camada média de integração, topo estreito de E2E (lentos, caros). Alternativa moderna: Testing Trophy (Kent C. Dodds) — dá mais peso à integração.

**Onde usar:** Definição de estratégia de testes; planejamento de CI/CD; argumentação com gestão sobre investimento em testes.

**Prós:** Guia prático para balancear velocidade e confiança; reduz custo de manutenção; otimiza feedback loop.

**Contras:** Modelo simplificado — domínios diferentes pedem proporções diferentes; testes de integração com `WebApplicationFactory` borram a fronteira entre unitário e integração.

---

## 3. BIBLIOTECAS E FERRAMENTAS DE TESTE .NET

---

### Moq

**Descrição:** Biblioteca de mocking mais popular do ecossistema .NET. Permite criar mocks de interfaces e classes virtuais com API fluente. Suporte a `Setup`, `Verify`, `Returns`, `Callback`.

**Onde usar:** Testes unitários para isolar dependências; verificação de interações (chamou método X com parâmetro Y).

**Prós:** API intuitiva e fluente; amplamente adotado; integração com qualquer framework de teste.

**Contras:** Controvérsia de telemetria no Moq 4.20 (SponsorLink); performance inferior ao NSubstitute em cenários massivos; não mocka métodos estáticos nem classes sealed (sem Fody/Source Generators).

---

### NSubstitute

**Descrição:** Alternativa ao Moq com sintaxe mais limpa e natural. Usa `Substitute.For<T>()` e configuração direta sem `.Setup()`. Cresceu em adoção após a controvérsia do Moq.

**Onde usar:** Mesmos cenários do Moq; equipes que preferem sintaxe mais enxuta; projetos migrando do Moq.

**Prós:** Sintaxe mais limpa (`sub.Method().Returns(x)`); sem controvérsias de telemetria; boa documentação; mensagens de erro claras.

**Contras:** Pode causar NSubstitute ambiguity exceptions em cenários com overloads complexos; mesmas limitações de sealed/static que Moq.

---

### FluentAssertions

**Descrição:** Biblioteca de assertivas fluentes que melhora a legibilidade dos testes. Substitui `Assert.Equal(expected, actual)` por `actual.Should().Be(expected)`. Suporta assertivas ricas para collections, strings, exceptions, objetos, HTTP responses, etc.

**Onde usar:** Qualquer projeto de teste .NET que valorize legibilidade; especialmente útil para assertivas complexas (coleções, objetos aninhados).

**Prós:** Mensagens de erro extremamente descritivas; API fluente e encadeável; extensível; suporta equivalência de objetos (`BeEquivalentTo`).

**Contras:** Mudança de licença na v7 (Apache → comercial para empresas com receita > $1M); dependência adicional; pode gerar assertivas longas demais se não for disciplinado.

---

### Bogus

**Descrição:** Biblioteca para geração de dados fake realistas. Suporta localizações (pt_BR, en_US), tipos complexos (nomes, endereços, CPFs, emails, datas) e integração com AutoFixture.

**Onde usar:** Setup de testes com dados representativos; seed de banco para testes de integração; geração de massa para load tests.

**Prós:** Dados realistas por locale; API fluente (`new Faker<Pedido>().RuleFor(...)` ); determinístico com seed; enorme variedade de data sets.

**Contras:** Pode obscurecer o que é relevante no teste (dados aleatórios dificultam debugging); se não usar seed, testes podem ser não-determinísticos.

---

### Testcontainers

**Descrição:** Biblioteca que sobe containers Docker programaticamente para testes de integração. Suporta SQL Server, PostgreSQL, Redis, RabbitMQ, Kafka, etc. O container sobe no setup e é destruído no teardown.

**Onde usar:** Testes de integração que precisam de dependências reais (banco, cache, broker); substituição de mocks de infra por instâncias reais descartáveis.

**Prós:** Testa contra a dependência real (não um mock); containers descartáveis e isolados; suporte a custom images; integração com xUnit/NUnit.

**Contras:** Requer Docker no agente de CI; startup de containers adiciona latência (5-30s); consome recursos; Docker-in-Docker pode ser problemático em alguns CIs.

**Exemplo .NET:**
```csharp
public class SqlFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    public string ConnectionString => _container.GetConnectionString();
    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

---

### Verify (Snapshot Testing)

**Descrição:** Biblioteca de snapshot/approval testing. Serializa o output (objeto, HTTP response, imagem) e compara com um snapshot aprovado. Se mudou, falha e mostra diff. Aceita: JSON, texto, imagens, HTML.

**Onde usar:** Detectar mudanças não intencionais em respostas de API; validar outputs complexos (relatórios, emails); regression testing de serialização.

**Prós:** Detecta qualquer mudança no output; diff visual facilita review; suporta scrubbers para remover dados voláteis (datas, GUIDs).

**Contras:** Snapshots precisam ser commitados no repo; mudanças intencionais exigem re-aprovar o snapshot; pode gerar falsos positivos em outputs com dados dinâmicos.

---

## 4. MÉTRICAS DE LATÊNCIA E PERFORMANCE

---

### Percentis de Latência (p50, p95, p99)

**Descrição:** Métricas estatísticas que descrevem a distribuição de tempos de resposta. p50 (mediana) = 50% dos requests foram mais rápidos que esse valor. p95 = 95% dos requests abaixo. p99 = 99% abaixo. A diferença entre p50 e p99 revela a "cauda longa" (tail latency) que a média esconde.

**Onde usar:** SLIs/SLOs de APIs e serviços; dashboards de observabilidade; alertas de degradação; capacity planning.

**Prós:** p50 mostra a experiência "típica"; p95/p99 capturam a experiência dos piores casos (que a média esconde); essenciais para SLAs realistas.

**Contras:** p99 pode ser ruidoso com volume baixo de requests; requer volume estatisticamente significativo; armazenar histogramas consome mais do que médias simples.

**Referência prática:**
| Percentil | O que revela | Uso típico |
|---|---|---|
| p50 | Experiência mediana do usuário | Baseline de performance |
| p95 | Experiência dos 5% mais lentos | SLO primário recomendado |
| p99 | Tail latency (1% pior caso) | Detecção de outliers, SLO agressivo |
| p99.9 | Casos extremos | Debugging de infra, GC pauses, cold starts |

---

### Throughput (RPS / TPS)

**Descrição:** Requests Per Second (RPS) ou Transactions Per Second (TPS). Mede a capacidade do sistema processar volume. Relaciona-se com latência: sob carga, throughput satura e latência cresce (Lei de Little: L = λW).

**Onde usar:** Capacity planning; load testing; dimensionamento de infra; alertas de saturação.

**Prós:** Métrica direta de capacidade; fácil de medir e monitorar; essencial para sizing.

**Contras:** RPS alto sem contexto de latência é enganoso; throughput varia com o tipo de operação; picos e médias contam histórias diferentes.

---

### SLI / SLO / SLA

**Descrição:** **SLI** (Service Level Indicator) = métrica medida (ex: latência p95). **SLO** (Service Level Objective) = meta interna (ex: p95 < 200ms em 99.5% do tempo). **SLA** (Service Level Agreement) = contrato externo com penalidades (ex: 99.9% uptime, se violar, crédito ao cliente). SLO deve ser mais agressivo que SLA para ter margem.

**Onde usar:** Definição de contratos com clientes; error budgets; alertas baseados em burn rate; priorização de investimento em reliability.

**Prós:** Linguagem comum entre engenharia, produto e negócio; error budget quantifica risco; SLOs focam investimento em confiabilidade onde importa.

**Contras:** Definir SLOs ruins é pior que não ter (falsa segurança); requer instrumentação madura; error budget pode ser gaming ("queimar budget" pré-release).

---

### Error Budget

**Descrição:** Quantidade de "erro permitido" calculada a partir do SLO. Se SLO é 99.9% de disponibilidade em 30 dias, o error budget é ~43 minutos de downtime permitido. Quando o budget se esgota, o time deve priorizar estabilidade sobre features.

**Onde usar:** SRE e confiabilidade; decisão de congelar deploys; priorização entre features e debt técnica.

**Prós:** Quantifica o trade-off entre velocidade de entrega e estabilidade; decisão objetiva, não emocional; alinha engenharia e produto.

**Contras:** Requer buy-in organizacional; pode gerar conflito entre times se mal implementado; sem observabilidade madura, o cálculo é impreciso.

---

### Apdex (Application Performance Index)

**Descrição:** Índice de 0 a 1 que classifica requests em: Satisfeito (< T), Tolerando (< 4T) e Frustrado (>= 4T), onde T é o threshold definido. Apdex = (Satisfeitos + Tolerando/2) / Total. Score de 0.94+ = Excelente, < 0.5 = Inaceitável.

**Onde usar:** Dashboards executivos; visão simplificada de saúde; Application Insights e New Relic reportam nativamente.

**Prós:** Número único e intuitivo para comunicar saúde do sistema; fácil de agregar e comparar serviços.

**Contras:** Esconde a distribuição real (dois serviços com mesmo Apdex podem ter perfis bem diferentes); o threshold T é arbitrário; percentis são mais precisos para engenharia.

---

## 5. OBSERVABILIDADE

---

### OpenTelemetry (OTel)

**Descrição:** Standard open-source vendor-neutral para coleta de telemetria: traces, métricas e logs. Define SDKs, APIs, protocolos (OTLP) e semantic conventions. No .NET, integra-se via `System.Diagnostics.Activity` (traces) e `System.Diagnostics.Metrics`.

**Onde usar:** Qualquer sistema distribuído que precisa de observabilidade; substituição de SDKs proprietários (AppInsights SDK, Datadog agent); padronização cross-platform.

**Prós:** Vendor-neutral (troca de backend sem mudar código); standard da indústria (CNCF graduated); instrumentação automática para ASP.NET Core, HttpClient, EF Core, etc.; um SDK para traces + métricas + logs.

**Contras:** Ainda em evolução em algumas áreas (logs spec estabilizou recentemente); overhead de aprendizado do modelo (spans, resources, attributes); exporters para cada backend; ecossistema de auto-instrumentation menos maduro que agents proprietários.

**Exemplo Azure/.NET:**
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddAzureMonitorTraceExporter(o => o.ConnectionString = "..."))
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddAzureMonitorMetricExporter(o => o.ConnectionString = "..."));
```

---

### Distributed Tracing

**Descrição:** Técnica que rastreia um request através de múltiplos serviços, criando um trace com spans que formam uma árvore de chamadas. Cada span tem: TraceId (identifica o trace completo), SpanId (identifica a operação), duração, status e atributos. Padrão W3C TraceContext propaga contexto via headers `traceparent`/`tracestate`.

**Onde usar:** Microsserviços; debugging de latência ponta a ponta; identificação de bottlenecks; correlação de erros entre serviços.

**Prós:** Visibilidade ponta a ponta; identifica exatamente onde o tempo é gasto; correlaciona logs com traces; essencial para debugging em arquiteturas distribuídas.

**Contras:** Requer instrumentação em todos os serviços; volume de dados pode ser alto (sampling é necessário); ferramentas de visualização têm curva de aprendizado.

**Stack Azure:** Application Insights, Azure Monitor, Jaeger, Zipkin.

---

### Structured Logging (Serilog)

**Descrição:** Logging onde cada evento é uma estrutura de dados (não uma string). Permite queries sobre propriedades individuais. Serilog é a biblioteca dominante no .NET: sinks para console, Seq, Application Insights, Elasticsearch, etc. Usa message templates (`"Pedido {PedidoId} criado em {Valor}"`) que preservam a estrutura.

**Onde usar:** Todo sistema .NET moderno; substituição de `Console.WriteLine` e `ILogger` com string interpolation; investigação de incidentes com queries em Log Analytics (KQL).

**Prós:** Queries poderosas (filtra por `PedidoId`, `UserId`, etc.); correlação com traces via `CorrelationId`; human-readable e machine-parseable; enriquecimento automático (thread, machine, environment).

**Contras:** Sinks precisam ser configurados; volume de logs pode gerar custo alto; structured != útil se os campos forem mal nomeados.

---

### Health Checks

**Descrição:** Endpoints que reportam a saúde do serviço e suas dependências. ASP.NET Core tem suporte built-in via `AddHealthChecks()`. Tipos: liveness (o processo está respondendo?), readiness (está pronto para receber tráfego?), startup (terminou de inicializar?).

**Onde usar:** Kubernetes probes (liveness, readiness, startup); load balancers; dashboards de status; Azure App Service health checks.

**Prós:** Detecção automática de instâncias unhealthy; Kubernetes remove do pool automaticamente; built-in no ASP.NET Core; extensível com checks custom (banco, Redis, blob storage).

**Contras:** Health checks superficiais dão falsa confiança; checks muito profundos podem causar cascading failures (se checar dependência que está lenta); precisa de threshold e timeout adequados.

---

## 6. AUTENTICAÇÃO E AUTORIZAÇÃO

---

### OAuth 2.0

**Descrição:** Framework de autorização delegada. Permite que um aplicativo (client) acesse recursos de um usuário em outro serviço (resource server) sem receber a senha do usuário. Define quatro grant types principais: Authorization Code (+ PKCE), Client Credentials, Device Code e Refresh Token. **Não é autenticação** — é autorização. Autenticação é feita pelo OpenID Connect (camada sobre o OAuth 2.0).

**Onde usar:** Login com provedores externos (Google, Microsoft, GitHub); APIs que precisam de delegação de acesso; comunicação machine-to-machine (Client Credentials); SPAs e mobile apps (Authorization Code + PKCE).

**Prós:** Standard da indústria; separa autenticação de autorização; suporte a scopes granulares; tokens de curta duração (access token) + renovação (refresh token); amplamente suportado (Entra ID, Auth0, Keycloak).

**Contras:** Complexo de implementar corretamente (muitos grant types e flows); vulnerável a misconfigurações (redirect URI abertos, falta de PKCE); token management (revogação, rotação) adiciona complexidade; especificação grande com muitas extensões (RFC 6749 + dezenas de RFCs complementares).

---

### OpenID Connect (OIDC)

**Descrição:** Camada de identidade sobre o OAuth 2.0. Adiciona o **ID Token** (JWT) que contém claims do usuário autenticado (sub, name, email, etc.). Define discovery (`.well-known/openid-configuration`), UserInfo endpoint e claims padronizadas. É o que transforma OAuth (autorização) em autenticação.

**Onde usar:** Login de usuários em aplicações web/mobile; SSO corporativo; federação de identidade; qualquer cenário onde você precisa saber quem o usuário é (não só o que ele pode acessar).

**Prós:** Standard para autenticação moderna; ID Token é auto-contido (verificável sem round-trip); discovery simplifica configuração; claims padronizadas reduzem ambiguidade.

**Contras:** Toda a complexidade do OAuth 2.0 + camada adicional; ID Token != access token (confusão comum); claims sensíveis no token exigem criptografia; logout federado é notoriamente difícil.

**Stack Azure:** Microsoft Entra ID (ex-Azure AD), `Microsoft.Identity.Web`, MSAL.

---

### JWT (JSON Web Token)

**Descrição:** Formato compacto de token com três partes: Header (algoritmo), Payload (claims) e Signature. Base64Url-encoded e assinado (JWS) ou criptografado (JWE). Usado como access token e ID token no OAuth 2.0/OIDC.

**Onde usar:** Access tokens para APIs; ID tokens (OIDC); transferência segura de claims entre partes; stateless authentication.

**Prós:** Auto-contido (resource server valida sem chamar o IdP); compacto (cabe em header HTTP); standard (RFC 7519); suporte a claims customizadas.

**Contras:** Não pode ser revogado individualmente (até expirar ou usar blocklist); payload é decodificável (não encriptado por padrão, só assinado); tamanho cresce com claims; overhead de criptografia assimétrica na validação.

---

### RBAC (Role-Based Access Control)

**Descrição:** Modelo de autorização baseado em papéis. Usuários são atribuídos a roles; roles possuem permissões. A decisão de acesso é: "O usuário tem a role X?". No .NET: `[Authorize(Roles = "Admin")]`. No Azure: Azure RBAC com roles built-in e custom (Owner, Contributor, Reader, etc.).

**Onde usar:** Sistemas com perfis bem definidos (Admin, Manager, Viewer); Azure resources (IAM); APIs com granularidade por role; cenários onde a estrutura organizacional mapeia diretamente para papéis.

**Prós:** Simples de entender e implementar; auditorias claras ("quem tem acesso a quê"); suporte nativo no ASP.NET Core e Azure; escalável com groups/roles hierárquicos.

**Contras:** Explosão de roles em sistemas complexos (role explosion); granularidade limitada (roles são "binárias" — tem ou não tem); não é ideal para regras dinâmicas ("só o autor do pedido pode cancelar"); mudanças de permissão exigem alterar atribuições de role.

---

### ABAC (Attribute-Based Access Control)

**Descrição:** Modelo de autorização baseado em atributos do sujeito (cargo, departamento), do recurso (tipo, dono, classificação), da ação e do contexto (horário, IP, localização). Avalia políticas: "Permitir se (user.department == resource.department AND action == 'read')". Mais flexível que RBAC.

**Onde usar:** Regras dinâmicas e contextuais ("pode acessar só em horário comercial"); multi-tenant com isolamento por atributo; compliance complexo (LGPD, HIPAA).

**Prós:** Granularidade fina sem explosão de roles; políticas expressivas e contextuais; centralização de decisões em policy engine.

**Contras:** Complexidade de implementação e manutenção; debugging de "por que foi negado?" é difícil; performance (avaliar N atributos por request); menos ferramental pronto no .NET (precisa de policy engine como OPA ou custom middleware).

---

### Policy-Based Authorization (ASP.NET Core)

**Descrição:** Modelo nativo do ASP.NET Core que vai além de roles. Define policies com requirements e handlers: `options.AddPolicy("CanCancel", p => p.RequireClaim("department", "finance"))`. Handlers avaliam claims, roles, e qualquer lógica custom. Permite combinar RBAC + ABAC.

**Onde usar:** Qualquer API ASP.NET Core com regras além de `[Authorize(Roles)]`; regras baseadas em claims, age gates, resource ownership; substituição gradual de RBAC puro.

**Prós:** Flexível (handlers podem acessar DbContext, HttpContext, resource); composável (policies + requirements); nativo do framework; testável.

**Contras:** Handlers podem virar mini-services complexos; sem UI de gerenciamento (diferente de RBAC no Azure Portal); debugging de chains de requirements exige logging.

**Exemplo .NET:**
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PedidoOwner", policy =>
        policy.Requirements.Add(new ResourceOwnerRequirement()));
});

public class ResourceOwnerHandler : AuthorizationHandler<ResourceOwnerRequirement, Pedido>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement,
        Pedido pedido)
    {
        if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) == pedido.UsuarioId)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
```

---

### API Key

**Descrição:** Credencial estática (string longa) enviada no header ou query string para identificar o chamador. Não identifica um usuário, identifica um **client/aplicação**. Usado para rate limiting, billing e controle de acesso básico.

**Onde usar:** APIs públicas com controle de acesso simples; integrações server-to-server sem usuário final; API Management (Azure APIM usa subscriptions keys).

**Prós:** Simples de implementar e consumir; sem fluxo de tokens; bom para identificação + rate limiting.

**Contras:** Sem granularidade (a key dá acesso ou não); se vazou, acesso total até rotação; não identifica o usuário final; não tem expiração automática (diferente de tokens); não é autenticação.

---

### Managed Identity (Azure)

**Descrição:** Identidade gerenciada pelo Azure atribuída a um recurso (App Service, Function, VM, Container Apps). Elimina a necessidade de armazenar credentials no código ou Key Vault. O Azure gera e rotaciona as credenciais automaticamente. Dois tipos: System-assigned (lifecycle do recurso) e User-assigned (lifecycle independente, compartilhável).

**Onde usar:** App Service acessando SQL Database, Key Vault, Storage, Service Bus; eliminação de connection strings com senha; cenários zero-secret.

**Prós:** Zero secrets no código ou config; rotação automática; integração nativa com quase todos os serviços Azure; audit trail via Entra ID; `DefaultAzureCredential` funciona em dev e prod.

**Contras:** Só funciona no Azure (não portável); debugging de permissões pode ser opaco ("403 mas parece que tem RBAC"); latência do primeiro token (cold path); user-assigned exige gerenciamento extra de lifecycle.

---

## 7. SEGURANÇA ADICIONAL

---

### CORS (Cross-Origin Resource Sharing)

**Descrição:** Mecanismo do browser que controla quais origens podem fazer requests cross-origin para uma API. Definido via headers (`Access-Control-Allow-Origin`, `Access-Control-Allow-Methods`, etc.). Preflight request (OPTIONS) verifica antes de enviar o request real.

**Onde usar:** APIs consumidas por SPAs em domínios diferentes; BFF que precisa aceitar requests do frontend; qualquer API HTTP acessada por browser.

**Prós:** Proteção contra requests cross-origin maliciosos; configurável por rota/controller; nativo no ASP.NET Core.

**Contras:** `AllowAnyOrigin` com `AllowCredentials` é vulnerável; configuração errada é uma das principais causas de "funciona no Postman mas não no browser"; preflight adiciona latência; pode confundir desenvolvedores iniciantes.

---

### HTTPS / TLS

**Descrição:** Transport Layer Security criptografa a comunicação entre client e server. HTTPS = HTTP sobre TLS. Garante confidencialidade (ninguém lê), integridade (ninguém altera) e autenticidade (o servidor é quem diz ser). Certificados gerenciados via Azure App Service Managed Certificates ou Key Vault.

**Onde usar:** Todo tráfego em produção — sem exceção. Enforce via `UseHttpsRedirection()` e HSTS.

**Prós:** Proteção contra man-in-the-middle; requisito para HTTP/2; certificados gratuitos (Let's Encrypt, App Service managed); requisito de compliance (PCI-DSS, LGPD).

**Contras:** Overhead marginal de TLS handshake (minimizado com TLS 1.3 e session resumption); certificados precisam de renovação (automática com managed); debugging de TLS pode ser complexo (certificate chains, pinning).

---

### Key Vault (Azure)

**Descrição:** Serviço gerenciado para armazenamento seguro de secrets (connection strings, API keys), certificados e chaves criptográficas. Integra com Managed Identity para acesso sem credentials. Suporte a versionamento, soft-delete, purge protection e audit logs.

**Onde usar:** Qualquer secret que hoje está em `appsettings.json` ou variáveis de ambiente; certificados TLS; chaves de criptografia; rotação automática de secrets.

**Prós:** Secrets fora do código e do repositório; integração com App Configuration; access policies ou RBAC granular; HSM-backed para chaves; audit trail completo.

**Contras:** Latência de rede para cada acesso (usar caching do SDK); custo por operação (pricing por transaction); complexidade de permissões (vault access policies vs Azure RBAC); throttling em alto volume.

---

### OWASP Top 10

**Descrição:** Lista das 10 categorias de vulnerabilidades mais críticas em aplicações web, mantida pela OWASP Foundation e atualizada periodicamente. Versão 2021: Broken Access Control (A01), Cryptographic Failures (A02), Injection (A03), Insecure Design (A04), Security Misconfiguration (A05), entre outros.

**Onde usar:** Code reviews focados em segurança; checklists de deploy; threat modeling; treinamento de desenvolvedores; requisitos de compliance.

**Prós:** Referência universalmente aceita; guia prático com mitigações; base para ferramentas de scanning (SAST/DAST); compliance (PCI, SOC 2 referencia OWASP).

**Contras:** Cobertura limitada a web apps (não cobre infra, mobile nativo em profundidade); pode criar falsa sensação de segurança ("cobri o top 10, estou seguro"); atualização a cada ~4 anos pode ficar defasada.

---

## 8. RESILIÊNCIA E ESTABILIDADE

---

### Circuit Breaker

**Descrição:** Padrão que impede chamadas repetidas a um serviço com falhas, evitando cascading failures. Três estados: Closed (normal), Open (bloqueia chamadas) e Half-Open (tenta uma chamada para verificar recuperação). No .NET: Polly ou `Microsoft.Extensions.Http.Resilience`.

**Onde usar:** Chamadas a APIs externas; comunicação entre microsserviços; qualquer integração que pode falhar temporariamente.

**Prós:** Previne cascading failures; dá tempo ao serviço dependente de se recuperar; fail fast em vez de timeout lento; reduz pressão no serviço degradado.

**Contras:** Threshold mal configurado pode abrir o circuito cedo demais (falsos positivos); estado half-open precisa de política de probing; complexidade de observabilidade (monitorar estado do circuito).

---

### Retry com Backoff Exponencial

**Descrição:** Reenvia requests que falharam com intervalos crescentes entre tentativas (1s, 2s, 4s, 8s) + jitter (randomização) para evitar thundering herd. No .NET: Polly `.WaitAndRetryAsync()` ou `Microsoft.Extensions.Http.Resilience` (.NET 8+).

**Onde usar:** Transient faults (429 Too Many Requests, 503, timeouts de rede); chamadas a Azure SDK (já tem retry built-in); qualquer operação idempotente que pode falhar temporariamente.

**Prós:** Recupera automaticamente de falhas transientes; jitter evita sincronização de retries entre instâncias; configurável por tipo de falha.

**Contras:** Sem idempotência, pode causar operações duplicadas; retry em operação não-transiente desperdiça tempo; backoff longo pode exceder timeout do chamador; precisa de budgeting (máximo de retries) para não travar o request.

---

### Bulkhead

**Descrição:** Isolamento de recursos por funcionalidade para evitar que uma falha em uma operação consuma todos os recursos do sistema. Analogia: compartimentos de um navio. Implementação: thread pools separados, semaphores, connection pools isolados.

**Onde usar:** APIs com múltiplas dependências de criticidades diferentes; isolamento entre tenants; proteção de operações críticas contra vizinhos ruidosos (noisy neighbor).

**Prós:** Falha contida a um compartimento; operações críticas continuam funcionando; proteção contra resource exhaustion.

**Contras:** Overhead de gerenciar múltiplos pools; sizing dos compartimentos é difícil (muito pequeno = rejeição desnecessária; muito grande = sem proteção); complexidade operacional.

---

### Rate Limiting

**Descrição:** Controle de volume de requests aceitos por período. Algoritmos: Fixed Window, Sliding Window, Token Bucket, Concurrency Limiter. No .NET 7+: `Microsoft.AspNetCore.RateLimiting` built-in. No Azure: APIM policies.

**Onde usar:** APIs públicas; proteção contra abuse; fair usage entre tenants; compliance com limites de downstream.

**Prós:** Protege contra DDoS e abuse; garante fair usage; nativo no ASP.NET Core 7+; configurável por endpoint, por usuário, por IP.

**Contras:** Rate limits muito agressivos bloqueiam uso legítimo; distributed rate limiting (múltiplas instâncias) requer store centralizado (Redis); usuários podem contornar via múltiplos IPs.

---

## 9. QUALIDADE E MÉTRICAS DE CÓDIGO

---

### Code Coverage

**Descrição:** Percentual de código executado durante os testes. Métricas: line coverage, branch coverage, method coverage, condition coverage. Ferramentas .NET: Coverlet (collector) + ReportGenerator (visualização). Integração com Azure DevOps, SonarQube, Codecov.

**Onde usar:** CI/CD pipelines com quality gates; dashboards de qualidade; identificação de código não testado.

**Prós:** Métrica objetiva e automatizável; identifica dead code e caminhos não testados; quality gate no PR (ex: "não merge se coverage < 80%").

**Contras:** Coverage alto ≠ testes bons (pode cobrir linhas sem assertivas úteis); métrica facilmente gamificável; branch coverage é mais útil que line coverage mas menos comum; foco excessivo em % pode gerar testes de baixo valor.

---

### Cyclomatic Complexity

**Descrição:** Métrica que conta o número de caminhos independentes no código. Cada `if`, `switch`, `for`, `while`, `catch`, `&&`, `||` adiciona um caminho. Valor 1-10 = simples; 11-20 = moderado; 21+ = complexo e difícil de testar/manter.

**Onde usar:** Code reviews; quality gates; refatoração dirigida (focar nos métodos mais complexos); análise estática (SonarQube, NDepend).

**Prós:** Indica dificuldade de teste e manutenção; correlaciona com probabilidade de bugs; direciona refatoração.

**Contras:** Não captura complexidade cognitiva real (switch com 20 cases simples vs nested ifs); pode penalizar guard clauses legítimas; métrica isolada não conta a história completa.

---

### SonarQube / SonarCloud

**Descrição:** Plataforma de análise estática contínua. Detecta bugs, vulnerabilidades, code smells e calcula dívida técnica. Quality Gates definem critérios de aprovação. Suporte a C#, SQL, JS, etc. SonarCloud é a versão SaaS.

**Onde usar:** CI/CD pipelines; PRs com quality gate automático; dashboards de dívida técnica; compliance de segurança (SAST).

**Prós:** Análise abrangente (bugs, security, smells, duplicação); quality gates automatizados; trends e technical debt estimado; integração com Azure DevOps e GitHub.

**Contras:** Falsos positivos requerem triagem; regras default podem ser ruidosas; versão on-premise (SonarQube) exige infra; custo da licença enterprise.

---

## 10. CONCEITOS DE INFRAESTRUTURA E DEPLOY

---

### Feature Flags (Feature Toggles)

**Descrição:** Mecanismo para habilitar/desabilitar funcionalidades em runtime sem deploy. Permite dark launches, canary releases, A/B testing e kill switches. No .NET: `Microsoft.FeatureManagement` + Azure App Configuration.

**Onde usar:** Releases progressivos; trunk-based development (commit na main com feature desligada); kill switch de funcionalidade problemática; A/B testing.

**Prós:** Desacopla deploy de release; rollback de feature sem rollback de código; targeting por usuário/grupo/percentual; Azure App Configuration gerencia centralizadamente.

**Contras:** Flags abandonadas viram dívida técnica (cleanup é essencial); complexidade combinatória (N flags = 2^N estados possíveis); testes precisam cobrir flag on e off; debugging mais complexo ("essa feature está ligada para esse usuário?").

---

### IaC (Infrastructure as Code)

**Descrição:** Definir e provisionar infraestrutura via código versionável, auditável e repetível. Ferramentas: Terraform, Bicep (Azure-native), Pulumi, ARM Templates. Abordagens: declarativa (descreve estado desejado) vs imperativa (descreve passos).

**Onde usar:** Todo provisionamento de infra Azure; CI/CD de ambientes (dev, staging, prod); disaster recovery (recreate from code); compliance (drift detection).

**Prós:** Repetibilidade (mesmo código = mesma infra); versionamento (git); review de mudanças via PR; drift detection; multi-ambiente consistente.

**Contras:** Curva de aprendizado de HCL/Bicep; state management (Terraform state file é crítico); destruição acidental se mal configurado; import de recursos existentes pode ser trabalhoso.

**Recomendação Azure:** Bicep para Azure-only (first-class support, deploy nativo). Terraform para multi-cloud ou equipes que já o adotam.

---

### Container Orchestration (Kubernetes / ACA)

**Descrição:** Gerenciamento automatizado de containers em escala: scheduling, scaling, networking, rolling updates, self-healing. Kubernetes é o standard de mercado. Azure Container Apps (ACA) é a abstração serverless sobre Kubernetes (KEDA + Envoy + Dapr), sem gerenciar o cluster.

**Onde usar:** Microsserviços em containers; workloads que precisam de auto-scaling baseado em eventos (KEDA); migração de VMs para containers; cenários que precisam de orquestração (service discovery, load balancing).

**Prós ACA:** Sem gerenciamento de cluster; auto-scale to zero; Dapr built-in para comunicação entre serviços; integração nativa com APIM e Entra ID; pricing por consumo.

**Contras ACA:** Menos controle que AKS; limitações em networking avançado; vendor lock-in Azure; debug local pode ser desafiador.

---

*Gerado como material de estudo. Formato: Termo → Descrição → Onde usar → Prós → Contras → Exemplos .NET/Azure quando aplicável.*
