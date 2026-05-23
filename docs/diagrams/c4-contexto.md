# C4 Level 1 — Diagrama de Contexto

**Pergunta que responde:** Quem usa o sistema Fluxo de Caixa e com quais sistemas externos ele conversa?

```mermaid
graph TB
    Comerciante["👤 <b>Comerciante (Carlos)</b><br/><i>Dono de mercearia que registra<br/>vendas e despesas, e consulta<br/>o saldo do dia</i>"]

    Sistema["🏦 <b>Sistema CashFlow</b><br/><i>Permite registrar lançamentos<br/>(débito/crédito) e consultar<br/>o saldo diário consolidado</i>"]

    SwaggerUI["📘 <b>Swagger UI</b><br/><i>Interface de descoberta e teste<br/>dos endpoints (incluído nas APIs)</i>"]

    Comerciante -->|"Registra lançamentos<br/>e consulta saldo<br/>(HTTPS/JSON)"| Sistema
    Sistema -.->|"Documentação<br/>auto-gerada"| SwaggerUI
    Comerciante -->|"Explora endpoints<br/>(HTTPS)"| SwaggerUI

    classDef person fill:#08427b,stroke:#052e56,color:#fff
    classDef system fill:#1168bd,stroke:#0b4884,color:#fff
    classDef external fill:#999999,stroke:#666666,color:#fff

    class Comerciante person
    class Sistema system
    class SwaggerUI external
```

## O que esse diagrama comunica

- **Único ator humano** no escopo do desafio: o comerciante (persona Carlos — § 2 da análise).
- **Nenhuma integração externa** no MVP (sem ERP, sem banco, sem gateway de pagamento). Integrações futuras são citadas como evolução em § 13.
- A interface de uso no MVP é o **Swagger UI** das próprias APIs — frontend Web é evolução documentada.

## Próximos níveis

- **Nível 2 (Containers):** [c4-containers.md](c4-containers.md) — quebra o "Sistema de Fluxo de Caixa" em APIs, broker e banco.
- **Nível 3 (Componentes):** detalha cada API internamente.
