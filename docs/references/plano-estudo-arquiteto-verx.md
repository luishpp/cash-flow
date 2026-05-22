# Plano de Estudo — Arquiteto de Software (Verx)

## Contexto

Vaga de Arquiteto de Software via Verx Tecnologia, provavelmente alocado em cliente do setor financeiro ou regulado. O perfil exige visão de governança arquitetural + profundidade técnica. A estratégia é reforçar os pontos de gap sem abandonar seus pontos fortes (.NET/C#, Azure, sustentação de sistemas críticos).

---

## Semana 1 — Fundamentos Arquiteturais e Governança

### Objetivo
Consolidar o vocabulário e os frameworks de arquitetura corporativa que a vaga exige.

### Tópicos

**Domínios da Arquitetura (Negócios, Dados, Sistemas, Tecnologia)**
- Entenda a visão do TOGAF ADM (Architecture Development Method): não precisa certificar, mas precisa saber explicar o ciclo e onde cada domínio se encaixa.
- Saiba diferenciar arquitetura de solução vs. arquitetura corporativa vs. arquitetura de software.
- Referência rápida: pesquise "TOGAF ADM cycle" e entenda as fases A-H.

**Requisitos Não-Funcionais (RNFs)**
- Taxonomia: disponibilidade, escalabilidade, resiliência, observabilidade, segurança, performance, manutenibilidade.
- Saiba traduzir RNFs em decisões arquiteturais concretas. Exemplo: "99.9% de disponibilidade" → implica redundância, health checks, circuit breaker, graceful degradation.
- Pratique com cenários: "O cliente quer latência < 200ms no P95 para uma API de consulta de saldo. Como você projeta isso?"

**Governança e Reutilização de Artefatos**
- Architecture Decision Records (ADRs): o que são, quando usar, template básico.
- Design System / Component Library como forma de reutilização no frontend.
- Shared Kernel e Bounded Contexts (DDD) como estratégia de reutilização no backend.
- Fitness Functions: testes automatizados que validam características arquiteturais.

### Exercício
Escreva 3 ADRs fictícios para decisões comuns: escolha de message broker, estratégia de autenticação, e estratégia de persistência.

---

## Semana 2 — Siglas e Paradigmas (O "Vocabulário Obrigatório")

### Objetivo
Dominar as 18 siglas listadas na vaga. Você precisa aplicar no mínimo 8.

### Mapa de Conhecimento

| Sigla | O que é | Seu gancho no .NET/Azure |
|-------|---------|--------------------------|
| **DDD** | Domain-Driven Design | Bounded Contexts, Aggregates, Value Objects. Relacione com a estrutura de projetos que você já faz. |
| **BDD** | Behavior-Driven Design | SpecFlow no .NET. Given/When/Then. |
| **FDD** | Feature-Driven Design | Metodologia iterativa por features. Menos comum, saiba explicar o conceito. |
| **MVC** | Model-View-Controller | ASP.NET MVC. Você vive isso. |
| **MVVM** | Model-View-ViewModel | WPF, Xamarin, MAUI. Data binding bidirecional. |
| **MVP** | Model-View-Presenter | Variação do MVC. Presenter controla a View. Menos usado em .NET moderno. |
| **BFF** | Backend for Frontend | Um API Gateway/serviço dedicado por tipo de client (mobile, web). Muito usado com microsserviços. |
| **EDA** | Event-Driven Architecture | Azure Service Bus, Event Grid, Event Hubs. Pub/Sub, CQRS, Event Sourcing. |
| **SOA** | Service-Oriented Architecture | Predecessor dos microsserviços. ESB, contratos de serviço, WCF (legado). |
| **HTTP** | Protocolo de transporte | REST APIs, status codes, headers, CORS. Trivial. |
| **MQTT** | Protocolo IoT | Lightweight pub/sub. Azure IoT Hub usa MQTT. |
| **AMQP** | Protocolo de mensageria | RabbitMQ, Azure Service Bus usam AMQP. |
| **JSON** | Formato de dados | Serialização padrão. System.Text.Json vs Newtonsoft. |
| **gRPC** | RPC de alta performance | Protobuf, HTTP/2, streaming. ASP.NET tem suporte nativo. Ideal para comunicação interna entre microsserviços. |
| **SaaS** | Software as a Service | Modelo de entrega (ex: Office 365). |
| **IaaS** | Infrastructure as a Service | VMs, redes virtuais (Azure VMs). |
| **PaaS** | Platform as a Service | Azure App Service, Azure SQL, Azure Functions. |
| **IaC** | Infrastructure as Code | Terraform, Bicep, ARM Templates. Pipelines de infra no Azure DevOps. |

### Suas 8+ seguras
DDD, MVC, BFF, EDA, SOA, HTTP, JSON, gRPC, SaaS, PaaS, IaC = **11 siglas** que você consegue defender com experiência real.

### Exercício
Para cada sigla, prepare uma frase de 30 segundos: "Usei [sigla] no contexto de [projeto] para resolver [problema]. O resultado foi [benefício]."

---

## Semana 3 — Design Patterns (Profundidade Prática)

### Objetivo
Dominar no mínimo 5 dos padrões listados, com exemplos concretos em C#.

### Padrões Prioritários (os que você mais usa)

**Dependency Injection + Inversion of Control**
- Você já vive isso no ASP.NET Core (IServiceCollection). Saiba explicar: lifetime (Transient, Scoped, Singleton), composição de serviços, testabilidade.
- Pergunte a si mesmo: "Por que DI importa em uma arquitetura de microsserviços?"

**Singleton**
- `services.AddSingleton<T>()`. Cache em memória, configuração, connection pools.
- Cuidados: thread safety, memory leaks, dificuldade de teste.

**Mediator**
- MediatR: Commands, Queries, Notifications, Pipeline Behaviors.
- Relacione com CQRS: Command handlers vs Query handlers.
- Exemplo: "Uso MediatR para desacoplar controllers de regras de negócio. Pipeline behaviors para cross-cutting concerns como logging e validação."

**Façade**
- Simplificar uma subsistema complexo atrás de uma interface unificada.
- Exemplo real: um serviço que orquestra chamadas a 3 APIs externas e expõe um endpoint único.

**Unit of Work**
- Entity Framework DbContext É um Unit of Work. SaveChanges() comita tudo ou nada.
- Saiba explicar o pattern independente do EF: agrupa operações de escrita em uma transação lógica.

**MVC / MVVM**
- MVC: ASP.NET. Request → Controller → Model → View.
- MVVM: Se já trabalhou com Blazor, WPF ou MAUI, use esse exemplo.

### Padrões Complementares (saiba explicar)

**Proxy** — Lazy loading, caching proxy, proteção de acesso. Castle DynamicProxy no .NET.

**Composite** — Árvore de objetos tratados uniformemente. Menu com submenus, regras de negócio compostas.

**Iterator** — `IEnumerable<T>` e `yield return` no C#. Você usa sem perceber.

**Visitor** — Double dispatch. Expression trees no .NET são um caso de Visitor.

### Exercício
Desenhe no papel (ou whiteboard) a arquitetura de um serviço de pagamento usando: Mediator para orquestração interna, Façade para integração com gateway de pagamento, Unit of Work para persistência, DI para composição.

---

## Semana 4 — Testes, CI/CD e API Management

### Testes

**Pirâmide de Testes**
- Unitários: xUnit + Moq/NSubstitute. Cobertura de regras de negócio.
- Integração: WebApplicationFactory (ASP.NET), TestContainers para banco real.
- E2E: Playwright ou Selenium. Fluxos críticos de negócio.
- Performance/Carga: k6, JMeter, ou Azure Load Testing.

**Testes de Mutação**
- Conceito: alterar o código-fonte automaticamente (mutations) e verificar se os testes quebram. Se não quebram, seus testes são fracos.
- Ferramenta .NET: **Stryker.NET**. Saiba explicar o conceito e o valor mesmo se não usou em produção.

**Dica de entrevista:** "Testes de mutação medem a qualidade dos seus testes, não a qualidade do código. Uso Stryker.NET para identificar assertions fracas e dead code em testes unitários."

### CI/CD (Azure DevOps)

Você já tem experiência. Reforce:
- Pipeline YAML vs Classic.
- Stages: Build → Test → Publish Artifact → Deploy (Dev → Staging → Prod).
- Estratégias de deploy: Blue/Green, Canary, Rolling.
- Gates e aprovações entre ambientes.
- Integração com SonarQube para quality gates.
- GitFlow vs Trunk-Based Development: saiba defender os trade-offs.

### API Management — Apigee vs Azure APIM

Este é o gap principal. Estratégia: posicione-se como quem domina os **conceitos** e já implementou no Azure APIM.

| Conceito | Azure APIM | Apigee |
|----------|-----------|--------|
| Proxy de API | ✅ API Gateway | ✅ API Proxy |
| Políticas (rate limit, transform, auth) | Policies (XML) | Policies (XML/JS) |
| Portal do Desenvolvedor | Built-in Developer Portal | Integrated Developer Portal |
| Analytics | Application Insights | Apigee Analytics |
| Monetização | Subscriptions + Products | Monetization API |
| Ambientes | Azure Environments | Apigee Environments (test, prod) |
| Deploy | ARM/Bicep/Terraform | Apigee Maven Plugin / CI |

**O que estudar sobre Apigee:**
1. Conceito de API Proxy, ProxyEndpoint e TargetEndpoint.
2. Fluxo de request/response: PreFlow → Conditional Flows → PostFlow.
3. Políticas comuns: VerifyAPIKey, OAuthV2, SpikeArrest, Quota, AssignMessage.
4. Apigee X (versão cloud-native no Google Cloud) vs Apigee Hybrid.
5. Developer Portal: como times publicam e consomem APIs.

**Frase-chave para a entrevista:** "Implementei governança de APIs no Azure APIM com versionamento semântico, rate limiting por produto, transformação de payload e developer portal para onboarding de times consumidores. Os conceitos são portáveis — Apigee resolve os mesmos problemas com uma taxonomia ligeiramente diferente, e estou me aprofundando na plataforma."

---

## Semana 5 — Simulação e Revisão

### Cenários Arquiteturais para Praticar

**Cenário 1: Decomposição de Monolito**
"Você herdou um monolito .NET Framework com 500k linhas. O cliente quer migrar para microsserviços. Como você aborda?"
- Strangler Fig Pattern
- Identificar bounded contexts via Event Storming
- Anti-Corruption Layer
- Migração incremental com BFF

**Cenário 2: Arquitetura Event-Driven**
"O cliente precisa processar 10k transações/segundo com auditoria completa. Como você projeta?"
- Event Sourcing + CQRS
- Azure Event Hubs ou Kafka para ingestão
- Projections para leitura
- Idempotência e ordering guarantees

**Cenário 3: Governança de APIs em escala**
"A organização tem 40 times publicando APIs sem padronização. Como você estabelece governança?"
- API Design Guidelines (naming, versionamento, error handling)
- API Gateway centralizado (Apigee/APIM)
- Linting de OpenAPI specs no CI
- Catálogo de APIs com developer portal
- Métricas: latência P95, error rate, adoption rate

**Cenário 4: Resiliência e Observabilidade**
"Um serviço crítico está tendo timeouts intermitentes em produção. Como você investiga e resolve?"
- Distributed tracing (OpenTelemetry, Application Insights)
- Circuit breaker (Polly no .NET)
- Retry policies com exponential backoff
- Health checks e readiness probes
- Dashboards: RED metrics (Rate, Errors, Duration)

### Checklist Final

- [ ] Consigo explicar TOGAF ADM em 2 minutos
- [ ] Consigo defender 8+ siglas com exemplos reais
- [ ] Consigo implementar mentalmente 5+ design patterns em C#
- [ ] Consigo comparar Apigee vs Azure APIM sem hesitar
- [ ] Consigo desenhar uma arquitetura de microsserviços no whiteboard
- [ ] Consigo explicar minha estratégia de testes (pirâmide + mutação)
- [ ] Consigo descrever um pipeline CI/CD completo no Azure DevOps
- [ ] Consigo responder cenários de trade-off sem dar respostas "de livro"

---

## Recursos Rápidos

| Tema | Recurso |
|------|---------|
| TOGAF resumido | The Open Group — TOGAF Standard (overview gratuito) |
| DDD tático | "Domain-Driven Design Quickly" (InfoQ, gratuito) |
| Apigee | Google Cloud Skills Boost — "API Design and Fundamentals of Google Cloud's Apigee API Platform" |
| Stryker.NET | stryker-mutator.io/docs/stryker-net/introduction |
| Design Patterns em C# | refactoring.guru (com exemplos em C#) |
| Arquitetura .NET | Microsoft Architecture Guides (eShopOnContainers) |
| Event-Driven | "Designing Event-Driven Systems" (Confluent, gratuito) |

---

*Última atualização: Maio 2026*
