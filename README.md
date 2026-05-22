# Fluxo de Caixa — Controle de Lançamentos e Consolidado Diário

Sistema para controle de fluxo de caixa diário de um comerciante, com registro de lançamentos (débitos e créditos) e consulta de saldo diário consolidado.

## Arquitetura

O sistema é composto por dois serviços independentes que se comunicam de forma assíncrona via mensageria:

- **API de Lançamentos** — registra débitos e créditos (write side)
- **API de Consolidado** — expõe o saldo diário consolidado (read side)
- **Worker de Consolidação** — consome eventos de lançamento e atualiza a projeção de saldo

O serviço de lançamentos **não depende** do consolidado. Se o Worker ou a API de Consolidado caírem, os lançamentos continuam operando normalmente e as mensagens ficam na fila até serem processadas.

```
                  ┌─────────────────┐               ┌─────────────────┐
                  │ API Lançamentos │               │ API Consolidado │
                  │   :5001         │               │   :5002         │
                  └───┬─────────┬───┘               └────────┬────────┘
                      │         │                            │
                      ▼         ▼                            ▼
               ┌──────────┐ ┌──────────┐           ┌────────────────┐
               │PostgreSQL│ │ RabbitMQ │           │  PostgreSQL    │
               │  :5432   │ │  :5672   │           │    :5432       │
               │ (db lanç)│ └────┬─────┘           │ (db consolid.) │
               └──────────┘      │                 └───────▲────────┘
                                 ▼                         │
                          ┌─────────────┐                  │
                          │   Worker    │──────────────────┘
                          │ Consolidação│
                          └─────────────┘
```

## Pré-requisitos

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) instalado e rodando
- Portas disponíveis: `5001`, `5002`, `5432`, `5672`, `15672`

Não é necessário ter .NET SDK, PostgreSQL ou RabbitMQ instalados localmente — tudo roda em containers.

## Como executar

### Windows (PowerShell)

```powershell
# Clonar o repositório
git clone https://github.com/<seu-usuario>/fluxo-de-caixa.git
cd fluxo-de-caixa

# Subir todos os serviços
docker compose up --build -d

# Verificar se tudo subiu
docker compose ps

# Ver logs em tempo real
docker compose logs -f
```

### macOS / Linux (Terminal)

```bash
# Clonar o repositório
git clone https://github.com/<seu-usuario>/fluxo-de-caixa.git
cd fluxo-de-caixa

# Subir todos os serviços
docker compose up --build -d

# Verificar se tudo subiu
docker compose ps

# Ver logs em tempo real
docker compose logs -f
```

### Acessos após subir

| Serviço | URL |
|---|---|
| API de Lançamentos (Swagger) | http://localhost:5001/swagger |
| API de Consolidado (Swagger) | http://localhost:5002/swagger |
| RabbitMQ Management | http://localhost:15672 (guest/guest) |

### Parar os serviços

```bash
docker compose down
```

Para remover também os volumes (dados do banco):

```bash
docker compose down -v
```

## Endpoints

### API de Lançamentos (`http://localhost:5001`)

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/api/lancamentos` | Registra um novo lançamento (débito ou crédito) |
| `GET` | `/api/lancamentos?data={yyyy-MM-dd}` | Lista lançamentos de uma data |
| `GET` | `/api/lancamentos/{id}` | Consulta um lançamento específico |

**Exemplo — registrar um crédito (venda):**

```bash
curl -X POST http://localhost:5001/api/lancamentos \
  -H "Content-Type: application/json" \
  -d '{
    "tipo": "credito",
    "valor": 150.00,
    "descricao": "Venda do dia"
  }'
```

**Exemplo — registrar um débito (despesa):**

```bash
curl -X POST http://localhost:5001/api/lancamentos \
  -H "Content-Type: application/json" \
  -d '{
    "tipo": "debito",
    "valor": 45.50,
    "descricao": "Compra de estoque"
  }'
```

### API de Consolidado (`http://localhost:5002`)

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/consolidado/{data}` | Saldo consolidado de uma data específica |
| `GET` | `/api/consolidado?de={data}&ate={data}` | Saldo consolidado por período |

**Exemplo — consultar saldo do dia:**

```bash
curl http://localhost:5002/api/consolidado/2025-05-22
```

**Resposta esperada:**

```json
{
  "data": "2025-05-22",
  "totalCreditos": 150.00,
  "totalDebitos": 45.50,
  "saldo": 104.50
}
```

## Executar os testes

```bash
# Testes unitários
docker compose run --rm api-lancamentos dotnet test

# Ou, se tiver o .NET SDK instalado localmente:
dotnet test ./tests/FluxoCaixa.Lancamentos.Tests
dotnet test ./tests/FluxoCaixa.Consolidado.Tests
```

## Stack tecnológica

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 8 / C# |
| API | ASP.NET Minimal APIs |
| ORM | Entity Framework Core + Npgsql |
| Banco de Dados | PostgreSQL |
| Mensageria | RabbitMQ + MassTransit |
| Rate Limiting | `Microsoft.AspNetCore.RateLimiting` |
| Resiliência | Polly (Retry + Circuit Breaker) |
| Testes | xUnit + Moq + FluentAssertions |
| Containers | Docker + Docker Compose |

## Documentação do projeto

| Documento | Descrição |
|---|---|
| [`docs/analise-desafio.md`](docs/analise-desafio.md) | Análise completa do desafio com decisões arquiteturais, diagramas C4, persona, jornada do usuário e estratégia de resiliência |
| [`docs/adrs/`](docs/adrs/) | Architecture Decision Records — registro de cada decisão técnica com justificativa |
| [`docs/diagrams/`](docs/diagrams/) | Diagramas C4 (Contexto, Containers, Componentes) |

## Estrutura do projeto

```
/src
  /FluxoCaixa.Lancamentos.API        API de Lançamentos
  /FluxoCaixa.Consolidado.API        API de Consolidado
  /FluxoCaixa.Consolidado.Worker     Worker de Consolidação
  /FluxoCaixa.Shared                 Contratos de eventos

/tests
  /FluxoCaixa.Lancamentos.Tests      Testes unitários e de integração
  /FluxoCaixa.Consolidado.Tests      Testes unitários e de integração

/docs                                Documentação arquitetural
docker-compose.yml                   Orquestração de todos os serviços
README.md                            Este arquivo
```
