# ADR-008: Docker Compose para execução local

**Status:** Aceita

## Contexto

O desafio exige explicitamente: *"Readme com instruções claras de como rodar localmente."* O avaliador precisa clonar o repositório, rodar um comando, e ter tudo funcionando — APIs, banco, broker, consumer.

## Decisão

Usar **Docker Compose** para orquestrar todos os componentes:

| Serviço | Imagem | Porta |
|---|---|---|
| `api-transactions` | Build local (Dockerfile) | 5001 |
| `api-balance` | Build local (Dockerfile) | 5002 |
| `postgres` | `postgres:16-alpine` | 5432 |
| `rabbitmq` | `rabbitmq:3-management-alpine` | 5672, 15672 |

Arquivo: [`../../docker-compose.yml`](../../docker-compose.yml).

## Trade-offs

| Ganha | Perde |
|---|---|
| Um comando para subir tudo (`docker compose up --build`) | Requer Docker Desktop instalado |
| Ambiente idêntico para qualquer avaliador | Primeiro build mais lento (restore + compile das imagens .NET) |
| Dependências isoladas — nada instalado na máquina do avaliador | Consome mais memória que rodar direto (~500MB total para todos os containers) |
| Imagens Alpine para minimizar tamanho | — |

## Health checks e dependências

O Docker Compose define `depends_on` com `condition: service_healthy` para que as APIs só subam após o PostgreSQL aceitar conexões e o RabbitMQ estar pronto. Isso evita erros de "connection refused" nos primeiros segundos.

```yaml
services:
  postgres:
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d cashflow"]
      interval: 5s
      retries: 10

  rabbitmq:
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "check_port_connectivity"]
      interval: 5s
      retries: 10

  api-transactions:
    depends_on:
      postgres:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
```

## Inicialização do PostgreSQL

O container Postgres aplica [`infra/postgres/init.sql`](../../infra/postgres/init.sql) automaticamente no primeiro startup, criando os usuários `app_transactions`/`app_balance` e os schemas `transactions`/`balance` com GRANTs restritos (ver [ADR-003](adr-003-postgres-schemas.md)).

## ADRs relacionadas

- [ADR-003](adr-003-postgres-schemas.md) — `init.sql` referenciado no volume do Postgres
- [ADR-004](adr-004-consumer-hostedservice.md) — 2 APIs em vez de 3 containers (consumer embarcado)
