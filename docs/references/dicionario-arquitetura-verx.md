# Dicionário de Termos — Preparação Arquiteto de Software

---

## 1. PADRÕES ARQUITETURAIS

---

### Strangler Fig Pattern

**Descrição:** Estratégia de migração incremental que envolve construir o novo sistema em torno do legado, redirecionando funcionalidades gradualmente até que o monolito seja completamente substituído ("estrangulado"). O nome vem da figueira estranguladora, que cresce ao redor de uma árvore hospedeira até substituí-la.

**Onde usar:** Transição de monolitos para microsserviços; modernização de sistemas legados (.NET Framework → .NET 8+); migração para cloud quando rewrite completo é inviável.

**Prós:** Risco controlado (migra uma funcionalidade por vez); o sistema legado continua operando durante a transição; permite validar a nova arquitetura em produção antes de comprometer tudo; rollback granular por funcionalidade.

**Contras:** Exige API Gateway ou proxy para rotear tráfego entre legado e novo; período de transição com dois sistemas em paralelo aumenta custo operacional; pode se arrastar indefinidamente sem governance firme; a "última milha" (desligar o monolito) costuma ser a mais difícil.

**Exemplo Azure/.NET:** Azure API Management na frente roteando /api/catalogo para o novo serviço em Container Apps e /api/pedidos ainda para o monolito IIS. Feature flags controlam a migração gradual de tráfego (10% → 50% → 100%).

---

### Anti-Corruption Layer (ACL)

**Descrição:** Camada intermediária que traduz e isola o modelo de domínio novo do modelo do sistema legado (ou externo). Impede que conceitos, contratos e "sujeira" do sistema antigo contaminem o design do novo.

**Onde usar:** Integração com sistemas legados durante migração (Strangler Fig); comunicação entre bounded contexts com modelos incompatíveis; integração com APIs de terceiros cujo contrato não se controla.

**Prós:** Protege o novo domínio de acoplamento indesejado; permite evoluir o novo sistema independentemente; centraliza a lógica de tradução em um único lugar.

**Contras:** Camada extra de código para manter; latência adicional na tradução; pode se tornar um gargalo se mal dimensionada.

**Exemplo Azure/.NET:** Um serviço intermediário que consome a API SOAP do ERP legado, traduz os DTOs para o modelo de domínio do novo bounded context, e expõe via REST/gRPC para os microsserviços modernos.

---

### CQRS (Command Query Responsibility Segregation)

**Descrição:** Separa o modelo de escrita (Commands — mutam estado) do modelo de leitura (Queries — retornam dados). Podem ser modelos diferentes no mesmo banco ou bancos completamente diferentes.

**Onde usar:** Sistemas com ratio leitura/escrita muito desbalanceado; cenários que exigem otimização independente de leitura e escrita; domínios com regras de escrita complexas mas consultas simples (ou vice-versa).

**Prós:** Escala leitura e escrita independentemente; read models desnormalizados otimizados para cada consulta específica; simplifica handlers de comando (focados em regras de negócio) e handlers de query (focados em performance).

**Contras:** Eventual consistency entre escrita e leitura; complexidade operacional maior (projeções, sincronização); mais código e infraestrutura para manter; pode ser over-engineering para CRUDs simples.

**Exemplo Azure/.NET:** MediatR com `IRequest<T>` para queries e `IRequest` para commands. Escrita em Azure SQL normalizado; leitura de Cosmos DB desnormalizado, atualizado por projeções assíncronas via Service Bus.

---

### Event Sourcing

**Descrição:** Em vez de persistir o estado atual de uma entidade, persiste todos os eventos que levaram ao estado atual. O estado é reconstruído fazendo replay da sequência de eventos. Cada evento é imutável.

**Onde usar:** Domínios com exigência regulatória de auditoria completa (financeiro, saúde, jurídico); sistemas que precisam reconstruir estado em qualquer ponto no tempo; cenários que alimentam múltiplas projeções a partir da mesma sequência de eventos.

**Prós:** Auditoria completa e nativa — histórico imutável; permite reconstruir estado em qualquer momento (time-travel); facilita alimentar read models diferentes; desacoplamento natural via eventos.

**Contras:** Complexidade significativa de implementação (versionamento de eventos, snapshots, replay); eventual consistency entre event store e projeções; curva de aprendizado alta; infraestrutura adicional (event store, projeções, idempotência).

**Exemplo Azure/.NET:** Cosmos DB com partition key por aggregate (append-only). Projeções via Azure Functions consumindo o Change Feed para alimentar read models em SQL ou Redis. Snapshots periódicos para evitar replay de milhares de eventos.

---

### Microservices

**Descrição:** Cada serviço é um deploy independente, com banco de dados próprio (database-per-service), responsável por um bounded context do domínio. Comunicação via APIs síncronas (REST/gRPC) ou mensageria assíncrona.

**Onde usar:** Domínios grandes com times independentes; quando diferentes partes do sistema têm perfis de escala, ciclos de release e requisitos regulatórios distintos; organizações com maturidade DevOps.

**Prós:** Deploy independente por time; escala granular por serviço; resiliência (falha isolada); liberdade tecnológica por serviço.

**Contras:** Custo operacional enorme (observabilidade distribuída, gestão de contratos, versionamento de APIs); complexidade de transações distribuídas (Saga pattern); latência de rede entre serviços; debugging distribuído é difícil.

**Exemplo Azure/.NET:** Cada serviço como Azure Container App ou App Service com seu Azure SQL Database. API Management como gateway. Service Bus para comunicação assíncrona. Application Insights com distributed tracing.

---

### Serverless

**Descrição:** Modelo onde o provedor cloud gerencia completamente a infraestrutura. O código executa em resposta a eventos (HTTP request, mensagem em fila, timer) e escala automaticamente de zero a milhares de instâncias.

**Onde usar:** Workloads event-driven com carga variável; processamento batch; webhooks; funções de integração entre sistemas; cenários onde o custo por execução é mais vantajoso que instância ociosa.

**Prós:** Sem gestão de infra; escala automática de zero; modelo pay-per-execution (paga só o que usa); deploy rápido.

**Contras:** Cold start (latência na primeira execução); limites de tempo de execução; vendor lock-in; debugging mais complexo; custo pode escalar inesperadamente com alto volume.

**Exemplo Azure/.NET:** Azure Functions com trigger de Service Bus para processar mensagens, Timer Trigger para jobs agendados, HTTP Trigger para APIs leves. Durable Functions para orquestrações stateful.

---

### Layered Architecture

**Descrição:** Organização clássica em camadas horizontais: Presentation → Business Logic → Data Access. Cada camada só referencia a camada imediatamente abaixo.

**Onde usar:** Aplicações de complexidade média; equipes que precisam de uma estrutura familiar e fácil de entender; quando não há exigência de desacoplamento avançado.

**Prós:** Simples de entender e implementar; padrão amplamente conhecido; separação de concerns básica.

**Contras:** Camada de negócio frequentemente depende da camada de dados (acoplamento); mudanças no banco propagam para cima; tende a virar "big ball of mud" em sistemas grandes; difícil de testar o domínio isoladamente.

---

### Onion Architecture

**Descrição:** Dependências apontam para dentro como camadas de uma cebola. O Domain Model fica no núcleo sem referência externa. Application Services em volta, Infrastructure e UI na camada mais externa. Inversão de dependência: o Domain define interfaces, Infrastructure implementa.

**Onde usar:** Domínios com regras de negócio complexas que mudam frequentemente; quando testabilidade do domínio isolado é prioridade; projetos de longa duração com evolução contínua.

**Prós:** Domínio completamente isolado de detalhes técnicos; alta testabilidade (testa sem banco, sem fila, sem HTTP); troca de ORM ou framework sem tocar o domínio.

**Contras:** Excesso de abstrações em domínios simples (interfaces para tudo); cerimônia de mapeamentos entre camadas; curva de aprendizado para times juniores.

**Exemplo .NET:** Core/Domain (entidades, value objects, interfaces), Application (use cases, DTOs), Infrastructure (EF Core, clients HTTP, Service Bus), API (controllers finos, configuração DI).

---

### Hexagonal Architecture (Ports & Adapters)

**Descrição:** O application core expõe Ports (interfaces) e o mundo externo conecta via Adapters. Driving Ports (primários): como o externo aciona a aplicação. Driven Ports (secundários): como a aplicação aciona o externo. Foco na simetria — a aplicação não sabe se é acionada por HTTP, fila ou teste.

**Onde usar:** Serviços que precisam ser acionados por múltiplos canais (REST, mensageria, timer); quando a intercambiabilidade de adaptadores é requisito real.

**Prós:** Simetria total — mesmo core para qualquer canal de entrada; altamente testável (injeta adapter fake); flexibilidade de substituição de tecnologias externas.

**Contras:** Na prática em .NET, produz estrutura muito parecida com Onion — a diferença é mais de mentalidade do que de pastas; mesmos riscos de over-engineering em domínios simples.

**Exemplo .NET:** Serviço de sinistros acionado por REST (controller ASP.NET), Service Bus trigger (Azure Function), e Timer Trigger. Três driving adapters, mesmo port, mesmo core.

---

### Clean Architecture

**Descrição:** Síntese de Robert C. Martin (Uncle Bob) dos princípios de Onion e Hexagonal. Regra única: a Dependency Rule — código interno nunca referencia externo. Círculos de dentro para fora: Entities → Use Cases → Interface Adapters → Frameworks & Drivers.

**Onde usar:** Projetos .NET de médio a grande porte com domínio não-trivial; equipes que querem estrutura padronizada e documentada.

**Prós:** Dependency Rule clara e fácil de comunicar; ecossistema de templates no .NET (Jason Taylor); combinação natural com MediatR para CQRS leve; amplamente documentado.

**Contras:** Cerimônia alta para microserviços simples; para um time de dois devs com três endpoints, gera mais fricção que valor; risco de seguir o template sem entender o porquê.

---

### Vertical Slice Architecture

**Descrição:** Organiza código por feature/funcionalidade em vez de por camada. Cada "slice" contém tudo que precisa: request, handler, validação, acesso a dados, resposta. Complexidade proporcional à necessidade de cada feature.

**Onde usar:** APIs com endpoints de natureza muito variada (CRUDs triviais + regras complexas no mesmo projeto); times que querem evitar abstrações uniformes forçadas.

**Prós:** Cada feature na complexidade que merece; CRUD simples é um handler de 15 linhas; feature complexa tem domain logic robusto; sem obrigação de N camadas de abstração.

**Contras:** Sem disciplina, vira coleção de scripts desconectados; cross-cutting concerns (logging, auth, transações) precisam de pipeline behaviors ou middleware para não duplicar.

---

### SOA (Service-Oriented Architecture)

**Descrição:** Predecessor dos microsserviços. Serviços comunicam via Enterprise Service Bus (ESB) com contratos formais. Foco em reutilização e composição de serviços.

**Onde usar:** Contexto histórico e integração com legados que ainda usam ESB; compreensão da evolução arquitetural.

**Prós:** Reutilização de serviços; contratos bem definidos; orquestração centralizada.

**Contras:** ESB como ponto único de falha e gargalo; acoplamento via bus; overhead de governança centralizada; menor agilidade que microsserviços.

**Exemplo .NET legado:** WCF services, BizTalk como ESB.

---

### EDA (Event-Driven Architecture)

**Descrição:** Componentes se comunicam via eventos (publicação e assinatura). Produtores emitem eventos sem saber quem consome. Desacoplamento temporal e espacial.

**Onde usar:** Sistemas distribuídos que precisam de desacoplamento forte; processamento assíncrono; integração entre bounded contexts; cenários de alta vazão.

**Prós:** Desacoplamento total entre produtor e consumidor; escala independente; resiliência (fila absorve picos); extensibilidade (adiciona consumidores sem alterar produtor).

**Contras:** Eventual consistency; debugging complexo (rastrear fluxo de eventos); ordenação de eventos pode ser desafiadora; dead letter queues precisam de tratamento.

**Exemplo Azure:** Service Bus (filas e tópicos), Event Grid (eventos de plataforma), Event Hubs (streaming de alto volume).

---

### BFF (Backend for Frontend)

**Descrição:** Um serviço backend dedicado para cada tipo de client (mobile, web, desktop). Cada BFF agrega e formata dados especificamente para as necessidades daquele frontend.

**Onde usar:** Quando diferentes clientes precisam de formatos, agregações e campos diferentes da mesma API; microsserviços com múltiplos frontends.

**Prós:** Frontend recebe exatamente o que precisa sem over-fetching; isola lógica de adaptação por canal; permite evolução independente por client.

**Contras:** Duplicação de lógica entre BFFs; mais serviços para manter e deployar.

---

## 2. DESIGN PATTERNS (GoF & Enterprise)

---

### Dependency Injection (DI) + Inversion of Control (IoC)

**Descrição:** Dependências são fornecidas (injetadas) externamente em vez de criadas internamente. IoC é o princípio; DI é o mecanismo. No ASP.NET Core, é nativo via `IServiceCollection`.

**Onde usar:** Toda aplicação .NET moderna. Fundamental para testabilidade, desacoplamento e composição de serviços.

**Prós:** Testabilidade (mocka dependências); desacoplamento entre implementação e contrato; gerenciamento de lifetime (Transient, Scoped, Singleton); composição flexível.

**Contras:** Curva de aprendizado para entender lifetimes; erros de configuração podem ser sutis (captive dependency); over-injection (constructor com 10 parâmetros indica violação de SRP).

---

### Singleton

**Descrição:** Garante uma única instância de uma classe durante o lifetime da aplicação. No .NET: `services.AddSingleton<T>()`.

**Onde usar:** Cache em memória, configuração, connection pools, clients HTTP reutilizáveis.

**Prós:** Instância compartilhada economiza recursos; ideal para estado global imutável ou read-only.

**Contras:** Thread safety obrigatório em cenários concorrentes; memory leaks se não gerenciado; dificulta testes (estado compartilhado entre testes).

---

### Mediator

**Descrição:** Desacopla quem envia uma solicitação de quem a processa. No .NET, MediatR é a implementação padrão: Commands, Queries, Notifications, Pipeline Behaviors.

**Onde usar:** Desacoplar controllers de regras de negócio; implementar CQRS leve; centralizar cross-cutting concerns (logging, validação) via pipeline behaviors.

**Prós:** Controllers finos (apenas delegam); handlers independentes e testáveis; pipeline behaviors para concerns transversais; natural para CQRS.

**Contras:** Indireção dificulta "Go to Definition"; pode mascarar complexidade; overhead de abstrações em projetos simples.

---

### Façade

**Descrição:** Interface simplificada para um subsistema complexo. Esconde a complexidade de múltiplas interações atrás de um ponto de entrada único.

**Onde usar:** Serviço que orquestra chamadas a múltiplas APIs externas; simplificação de bibliotecas complexas; unificação de subsistemas para o consumidor.

**Prós:** Simplifica uso para o cliente; reduz acoplamento com subsistemas internos; ponto único de manutenção.

**Contras:** Pode virar "God class" se absorver responsabilidade demais; esconde complexidade mas não a elimina.

---

### Unit of Work

**Descrição:** Agrupa operações de banco em uma transação única. Commits tudo ou nada. No .NET, o `DbContext` do Entity Framework **é** um Unit of Work — `SaveChanges()` persiste tudo em uma transação.

**Onde usar:** Qualquer operação que envolva múltiplas escritas que precisam ser atômicas.

**Prós:** Consistência transacional; evita escritas parciais; natural no EF Core.

**Contras:** Escopo do DbContext precisa ser bem gerenciado (Scoped no DI); transações longas podem causar locks; em microsserviços, não funciona cross-service (precisa de Saga).

---

### Repository

**Descrição:** Abstração que encapsula a lógica de acesso a dados, expondo uma interface orientada a coleções. O domínio trabalha com `IOrderRepository` sem saber se é EF Core, Dapper ou API externa.

**Onde usar:** Quando é necessário trocar ou combinar strategies de acesso a dados; isolar o domínio de detalhes de persistência; facilitar testes com repositórios in-memory.

**Prós:** Domínio desacoplado do ORM; testabilidade; ponto único de acesso a dados por aggregate.

**Contras:** No .NET, `DbContext` + `DbSet<T>` já é uma abstração sobre o banco — repositório em cima pode ser redundante; risco de "leaky abstraction" (expor IQueryable).

---

### Observer

**Descrição:** Define dependência um-para-muitos: quando um objeto muda de estado, todos os dependentes são notificados. No .NET: eventos/delegates nativos, MediatR Notifications, ou domain events.

**Onde usar:** Domain events (ex: `OrderPlaced` notifica estoque, faturamento e email); reações desacopladas a mudanças de estado.

**Prós:** Desacoplamento entre quem muda e quem reage; extensível (adiciona subscribers sem alterar o publisher).

**Contras:** Fluxo difícil de rastrear; ordering de notificações pode ser imprevisível; memory leaks se subscriptions não são desregistradas.

---

### Strategy

**Descrição:** Define uma família de algoritmos intercambiáveis. O client escolhe qual usar em runtime. No .NET, tipicamente via interfaces injetadas por DI.

**Onde usar:** Múltiplas formas de cálculo (frete, desconto, imposto); diferentes provedores de pagamento; regras que variam por tenant ou configuração.

**Prós:** Open/Closed Principle (adiciona estratégias sem modificar código existente); testabilidade; flexibilidade em runtime.

**Contras:** Número de classes cresce com as estratégias; o client precisa conhecer as opções (ou usar factory).

---

### Saga

**Descrição:** Padrão para gerenciar transações distribuídas em microsserviços. Em vez de uma transação ACID cross-service, uma sequência de transações locais com compensações em caso de falha. Dois estilos: Orquestração (um coordenador central) e Coreografia (cada serviço reage a eventos).

**Onde usar:** Qualquer operação que envolva múltiplos microsserviços com bancos independentes (ex: pedido que afeta estoque, pagamento e entrega).

**Prós:** Permite consistência eventual sem transações distribuídas (2PC); cada serviço mantém autonomia e banco próprio.

**Contras:** Compensações podem ser complexas de implementar ("desfazer" nem sempre é trivial); eventual consistency; debugging e observabilidade difíceis; orquestração pode virar ponto único de falha.

---

## 3. PARADIGMAS DE DESIGN

---

### DDD (Domain-Driven Design)

**Descrição:** Abordagem que coloca o domínio de negócio no centro do design do software. Divide em aspectos estratégicos (Bounded Contexts, Context Maps, Ubiquitous Language) e táticos (Aggregates, Entities, Value Objects, Domain Events, Repositories).

**Onde usar:** Domínios complexos com regras de negócio ricas; sistemas de longa duração; equipes grandes que precisam definir fronteiras claras entre subdomínios.

**Prós:** Alinhamento do código com o negócio; Bounded Contexts definem fronteiras naturais para microsserviços; Ubiquitous Language reduz ambiguidade; alta testabilidade do domínio.

**Contras:** Overhead significativo para domínios simples (CRUDs); curva de aprendizado alta; exige colaboração próxima com domain experts; modelagem tática pode ser over-engineering em contextos triviais.

---

### BDD (Behavior-Driven Design)

**Descrição:** Extensão do TDD focada em comportamento do sistema em linguagem de negócio. Especificações no formato Given/When/Then. No .NET: SpecFlow.

**Onde usar:** Cenários onde stakeholders de negócio validam critérios de aceite; documentação viva de regras de negócio; testes de integração legíveis.

**Prós:** Especificações legíveis por não-técnicos; documentação viva; testes alinhados com requisitos de negócio.

**Contras:** Manutenção de step definitions; overhead de setup; pode ser desnecessário quando o time é puramente técnico.

---

### FDD (Feature-Driven Design)

**Descrição:** Metodologia iterativa que organiza o desenvolvimento por features de negócio. Cada feature é pequena, mensurável e entregável. Ciclo: modelar domínio → listar features → planejar por feature → design por feature → build por feature.

**Onde usar:** Times que precisam de rastreabilidade de progresso por feature; gerência que quer visibilidade granular; contextos onde features são a unidade de valor.

**Prós:** Progresso visível e granular; foco em entrega de valor; funciona bem com times grandes.

**Contras:** Menos usado na prática moderna; overhead de planejamento; menos adaptável que Scrum/Kanban para mudanças rápidas.

---

### MVC (Model-View-Controller)

**Descrição:** Separa aplicação em Model (dados/regras), View (apresentação) e Controller (coordenação). O controller recebe input, manipula o model e seleciona a view.

**Onde usar:** Aplicações web com renderização server-side (ASP.NET MVC); APIs REST (controller recebe request, manipula service, retorna response).

**Prós:** Separação clara de concerns; padrão amplamente adotado e documentado; natural no ASP.NET.

**Contras:** Controllers tendem a engrossar (fat controllers); em SPAs modernas o MVC server-side perde relevância; View e Controller podem acoplar.

---

### MVVM (Model-View-ViewModel)

**Descrição:** Model (dados/regras), View (UI), ViewModel (lógica de apresentação com data binding bidirecional). A View se liga ao ViewModel; mudanças são refletidas automaticamente.

**Onde usar:** WPF, Xamarin, .NET MAUI; aplicações com UI rica e data binding intensivo.

**Prós:** Data binding bidirecional elimina código de sincronização; ViewModel testável sem UI; separação limpa entre design e lógica.

**Contras:** Complexidade do binding em cenários avançados; debugging de bindings pode ser opaco; overhead para UIs simples.

---

## 4. FRAMEWORKS E GOVERNANÇA

---

### TOGAF (The Open Group Architecture Framework)

**Descrição:** Framework de arquitetura corporativa que define o ADM (Architecture Development Method) — ciclo iterativo de fases (Preliminary, A-H) cobrindo desde visão estratégica até governança de mudanças. Inclui Architecture Repository para reutilização de artefatos.

**Onde usar:** Organizações que precisam alinhar TI com estratégia de negócio; definição de roadmaps de transformação; governança de portfólio de sistemas.

**Prós:** Abordagem completa e estruturada; Architecture Repository promove reutilização; amplamente reconhecido no mercado; adaptável (não precisa seguir 100%).

**Contras:** Pesado e burocrático se seguido à risca; requer maturidade organizacional; certificação cara; muitas organizações adotam apenas partes (repositório + metamodelo).

---

### TOGAF ADM (Architecture Development Method)

**Descrição:** Ciclo iterativo do TOGAF com fases: Preliminary → A (Architecture Vision) → B (Business Architecture) → C (Information Systems) → D (Technology Architecture) → E (Opportunities & Solutions) → F (Migration Planning) → G (Implementation Governance) → H (Change Management). Requirements Management é transversal a todas.

**Onde usar:** Projetos de transformação digital; modernização de legados; criação de roadmaps de migração.

**Prós:** Estrutura cada fase com entregáveis claros; gap analysis entre as-is e to-be; não é waterfall — permite iteração por camada.

**Contras:** Ciclo completo pode ser lento; exige sponsor executivo; muitas organizações simplificam drasticamente.

---

### Zachman Framework

**Descrição:** Esquema de classificação (não um processo) que organiza artefatos arquiteturais em uma matriz: perspectivas (escopo, modelo de negócio, lógico, tecnologia, implementação) × interrogações (o quê, como, onde, quem, quando, por quê).

**Onde usar:** Catalogar e localizar artefatos existentes; taxonomia para organizar documentação arquitetural.

**Prós:** Útil como mapa mental para saber "o que documentar"; completo como classificação.

**Contras:** Não prescreve como produzir os artefatos; mais teórico que prático; não define processos.

---

### ArchiMate

**Descrição:** Linguagem de modelagem padronizada pelo Open Group, complementar ao TOGAF. Representa camadas de negócio, aplicação e tecnologia com notação consistente.

**Onde usar:** Modelagem visual de arquitetura corporativa; documentação portável entre ferramentas; comunicação entre equipes técnicas e de negócio.

**Prós:** Notação padronizada e portável; cobre todas as camadas (negócio, aplicação, tecnologia); integra com ferramentas como Archi.

**Contras:** Curva de aprendizado; nem todos os stakeholders entendem a notação; pode ser overkill para equipes pequenas.

---

### ADR (Architecture Decision Record)

**Descrição:** Documento leve que registra uma decisão arquitetural com contexto, consequências e status. Armazenado junto ao código (repositório Git). Template: Título, Status, Contexto, Decisão, Consequências.

**Onde usar:** Toda decisão arquitetural significativa (escolha de message broker, estratégia de autenticação, estratégia de persistência); onboarding de novos membros do time.

**Prós:** Pragmático e leve; rastreabilidade de "por que decidimos X"; evita rediscutir decisões já tomadas; reutilizável (equipes consultam ADRs antes de decisões similares).

**Contras:** Exige disciplina para manter atualizado; pode acumular sem revisão; status stale se não for mantido.

---

### Fitness Functions

**Descrição:** Testes automatizados que validam características arquiteturais continuamente. Garantem que a arquitetura não degrada ao longo do tempo.

**Onde usar:** CI/CD pipelines para verificar regras arquiteturais (dependências entre camadas, convenções de código, limites de acoplamento).

**Prós:** Validação automatizada e contínua; previne degradação arquitetural; documentação executável das regras.

**Contras:** Esforço de setup; pode gerar falsos positivos; precisa de manutenção conforme a arquitetura evolui.

**Exemplo .NET:** Architecture tests com NetArchTest verificando que Domain não referencia Infrastructure.

---

### Backstage (Spotify)

**Descrição:** Portal de desenvolvedor open source que funciona como catálogo de componentes, APIs, documentação e templates. Resolve o problema de saber o que já existe antes de construir algo novo.

**Onde usar:** Organizações com muitos times e microsserviços; padronização de templates de projeto; catálogo de APIs e ownership.

**Prós:** Catálogo centralizado; templates padronizados; visibilidade de ownership; extensível via plugins (Azure DevOps, GitHub).

**Contras:** Setup e manutenção do portal; exige adoção organizacional; plugins podem ter qualidade variável.

---

## 5. PROTOCOLOS E TECNOLOGIAS

---

### gRPC

**Descrição:** Framework RPC de alta performance usando Protocol Buffers (protobuf) e HTTP/2. Suporta streaming bidirecional. ASP.NET tem suporte nativo.

**Onde usar:** Comunicação interna entre microsserviços onde performance é crítica; streaming de dados; cenários que exigem contratos fortemente tipados.

**Prós:** Alta performance (binário, HTTP/2); contratos definidos em .proto (code generation); streaming; multiplexing.

**Contras:** Não é browser-friendly (precisa de gRPC-Web); debugging mais difícil que REST (payload binário); curva de aprendizado de protobuf.

---

### AMQP (Advanced Message Queuing Protocol)

**Descrição:** Protocolo de mensageria usado por Azure Service Bus e RabbitMQ. Define modelo de filas, tópicos, exchanges e bindings.

**Onde usar:** Comunicação assíncrona entre serviços; decoupling temporal; garantia de entrega de mensagens.

**Prós:** Garantia de entrega; suporte a pub/sub e point-to-point; interoperável entre plataformas.

**Contras:** Complexidade de configuração; overhead de protocolo; requer broker de mensageria.

---

### MQTT

**Descrição:** Protocolo lightweight de pub/sub, otimizado para IoT e cenários com banda limitada. Azure IoT Hub usa MQTT.

**Onde usar:** Dispositivos IoT; telemetria; cenários com conectividade intermitente; baixo consumo de banda.

**Prós:** Extremamente leve; funciona em redes instáveis; QoS configurável; ideal para milhares de dispositivos.

**Contras:** Sem suporte nativo a request/response; funcionalidades limitadas comparado a AMQP; segurança requer camadas adicionais.

---

## 6. MODELOS DE CLOUD

---

### IaaS (Infrastructure as a Service)

**Descrição:** Provedor fornece infraestrutura básica: VMs, redes, storage. Você gerencia sistema operacional, runtime, aplicação.

**Onde usar:** Workloads legados que exigem controle do SO; licenças de software que requerem VM dedicada; migração lift-and-shift.

**Prós:** Controle total; flexibilidade; compatível com qualquer stack.

**Contras:** Responsabilidade de patching, segurança do SO, escalabilidade manual; mais custoso operacionalmente.

**Exemplo Azure:** Azure VMs, Virtual Machine Scale Sets.

---

### PaaS (Platform as a Service)

**Descrição:** Provedor gerencia SO, runtime e infraestrutura. Você gerencia apenas aplicação e dados.

**Onde usar:** Aplicações web e APIs; bancos de dados gerenciados; cenários onde o time quer focar no código sem gerenciar infra.

**Prós:** Menor overhead operacional; patching automático; escala facilitada; foco no código.

**Contras:** Menor controle sobre o ambiente; possíveis limitações de customização; vendor lock-in.

**Exemplo Azure:** App Service, Azure SQL Database, Azure Functions.

---

### SaaS (Software as a Service)

**Descrição:** Software entregue como serviço pronto. O provedor gerencia tudo — infra, aplicação, dados.

**Onde usar:** Ferramentas que não fazem parte do core business (email, CRM, CI/CD); consumo de APIs de terceiros.

**Prós:** Zero gestão; pronto para uso; atualizações automáticas.

**Contras:** Sem customização profunda; dependência total do fornecedor; dados em infraestrutura de terceiro.

**Exemplo:** Office 365, Salesforce, GitHub.

---

### IaC (Infrastructure as Code)

**Descrição:** Definir e provisionar infraestrutura via código declarativo ou imperativo, versionado em repositório. Permite reproducibilidade e automação.

**Onde usar:** Todo ambiente cloud que precisa de reproducibilidade; pipelines de infra; disaster recovery; ambientes efêmeros de teste.

**Prós:** Infraestrutura versionada e auditável; reprodutível; automação de deploy; consistência entre ambientes.

**Contras:** Curva de aprendizado; state management (Terraform state); drift entre código e realidade; debugging de falhas de provisioning.

**Exemplo Azure:** Bicep, ARM Templates, Terraform. Pipelines de infra no Azure DevOps.

---

## 7. REQUISITOS NÃO-FUNCIONAIS (RNFs)

---

### Disponibilidade

**Descrição:** Percentual de tempo que o sistema está operacional e acessível. Medida em "noves" (99.9%, 99.99%). Implica decisões de redundância, failover e zero-downtime deploys.

**Métricas:** Uptime percentual, MTTR, MTBF, taxa de health checks falhando.

**Decisões típicas:** Multi-região com failover, deployment slots (blue/green), health checks, readiness probes.

---

### Escalabilidade

**Descrição:** Capacidade de aumentar (scale-up) ou distribuir (scale-out) capacidade em resposta a demanda sem degradação.

**Métricas:** RPS, CPU/memória por instância, queue depth, tempo de scale-out, custo por transação.

**Decisões típicas:** HPA/KEDA no AKS, autoscale em App Service, CQRS para separar cargas, cache Redis.

---

### Resiliência

**Descrição:** Capacidade de se recuperar de falhas e continuar operando de forma degradada porém funcional.

**Métricas:** Circuit breaker trips, retry count, fallback activation, tempo de recuperação, DLQ depth, blast radius.

**Decisões típicas:** Polly (retry, circuit breaker, bulkhead, fallback), DLQ no Service Bus, Chaos Studio, feature flags para graceful degradation.

---

### Observabilidade

**Descrição:** Capacidade de entender o estado interno do sistema a partir de seus outputs (logs, métricas, traces). Pilar fundamental para operação de microsserviços.

**Métricas:** Latência p50/p95/p99 por endpoint, taxa de erro, trace duration ponta a ponta, métricas de negócio.

**Stack Azure/.NET:** Application Insights, OpenTelemetry, Log Analytics (KQL), Azure Monitor, Serilog, distributed tracing com W3C TraceContext.

---

### Segurança

**Descrição:** Proteção contra acessos não autorizados, vazamento de dados e vulnerabilidades. Abrange autenticação, autorização, criptografia, scanning e compliance.

**Métricas:** Vulnerabilidades abertas por severidade, tempo de remediação, auth failures, compliance score, secret rotation age.

**Stack Azure/.NET:** Entra ID (OAuth 2.0/OIDC), Key Vault, Defender for Cloud, Azure Policy, RBAC, Managed Identities.

---

### Performance

**Descrição:** Tempo de resposta e throughput sob carga. Medida em latência (p50, p95, p99) e requisições por segundo.

**Decisões típicas:** Cache (Redis), CDN, database tuning, connection pooling, async/await, output caching, índices.

---

### Manutenibilidade

**Descrição:** Facilidade de modificar, corrigir e evoluir o sistema ao longo do tempo. Impacta diretamente a velocidade de entrega de novas features.

**Decisões típicas:** Clean Architecture, testes automatizados, ADRs, code review, linting, fitness functions, modularidade.

---

## 8. CI/CD E DEPLOY

---

### Blue/Green Deployment

**Descrição:** Dois ambientes idênticos (blue = atual, green = novo). Deploy no green, valida, roteia tráfego do blue para green atomicamente. Rollback é trocar de volta.

**Prós:** Rollback instantâneo; zero downtime; validação completa antes do switch.

**Contras:** Custo duplicado de infra durante a transição; migrações de banco precisam ser compatíveis com ambas versões.

---

### Canary Deployment

**Descrição:** Nova versão liberada para um percentual pequeno de tráfego (5-10%). Monitora métricas; se saudável, aumenta gradualmente. Se detectar problemas, redireciona 100% para a versão antiga.

**Prós:** Risco mínimo (blast radius controlado); validação com tráfego real; detecção precoce de problemas.

**Contras:** Requer infraestrutura de roteamento de tráfego; complexidade de monitoramento; duas versões em produção simultâneas.

---

### Rolling Deployment

**Descrição:** Atualiza instâncias uma a uma (ou em batches). A cada momento, parte roda a versão nova e parte a antiga, até completar.

**Prós:** Sem custo extra de infra; gradual; suportado nativamente no Kubernetes e App Service.

**Contras:** Durante o rollout, versões misturadas podem causar inconsistências; rollback mais lento; requer backward compatibility.

---

### GitFlow vs Trunk-Based Development

**GitFlow:** Branches de feature, develop, release e hotfix. Bom para releases versionadas com ciclos longos. Complexo para CI/CD contínuo.

**Trunk-Based:** Commits direto na main (ou feature branches de vida curta). Feature flags para código inacabado. Ideal para entrega contínua. Exige disciplina e boa cobertura de testes.

---

## 9. API MANAGEMENT

---

### API Gateway

**Descrição:** Ponto de entrada centralizado para todas as APIs. Gerencia roteamento, autenticação, rate limiting, transformação de payload, versionamento e analytics.

**Azure APIM:** Policies em XML, Developer Portal, integração com Application Insights, subscriptions por produto.

**Apigee:** API Proxy com ProxyEndpoint e TargetEndpoint, fluxo PreFlow → Conditional → PostFlow, políticas (VerifyAPIKey, OAuthV2, SpikeArrest, Quota).

---

## 10. TIPOS DE ARQUITETO

---

### Arquiteto de Software

**Escopo:** Um sistema ou componente. Decisões técnicas internas: padrões de código, camadas, dependências, testabilidade, manutenibilidade. Horizonte: ciclo de vida do software.

---

### Arquiteto de Solução

**Escopo:** Um projeto ou iniciativa. Orquestra múltiplos sistemas para resolver um problema de negócio. Define componentes (Service Bus, APIM, Functions), contratos entre times, RNFs de ponta a ponta. Horizonte: médio prazo. Ponte entre negócio e implementação.

---

### Arquiteto Corporativo (Enterprise)

**Escopo:** Organização inteira. Portfólio de sistemas, padronização de plataforma, governança, racionalização de aplicações, alinhamento TI × negócio. Usa TOGAF, Zachman. Horizonte: longo prazo.

---

*Última atualização: Maio 2026 — Projeto Preparação Arquiteto de Software (Verx)*
