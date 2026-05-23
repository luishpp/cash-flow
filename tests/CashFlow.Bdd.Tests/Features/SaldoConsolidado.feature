# language: pt-BR
@balance @bdd
Funcionalidade: Saldo consolidado diário
  Como comerciante (Carlos)
  Quero ver meu saldo consolidado a cada lançamento aplicado
  Para saber rapidamente se o dia fechou no azul ou no vermelho

  Contexto:
    Dado que o saldo do dia "2026-05-23" está zerado

  Cenário: Aplicar um crédito atualiza o total de créditos e o saldo
    Quando eu aplico um crédito de 1500.00 no saldo do dia "2026-05-23"
    Então o total de créditos do dia "2026-05-23" deve ser 1500.00
    E o saldo do dia "2026-05-23" deve ser 1500.00

  Cenário: Aplicar um débito reduz o saldo proporcionalmente
    Dado que apliquei um crédito de 1000.00 no saldo do dia "2026-05-23"
    Quando eu aplico um débito de 300.00 no saldo do dia "2026-05-23"
    Então o total de débitos do dia "2026-05-23" deve ser 300.00
    E o saldo do dia "2026-05-23" deve ser 700.00

  Cenário: Crédito com valor não positivo é rejeitado
    Quando eu tento aplicar um crédito de 0.00 no saldo do dia "2026-05-23"
    Então uma DomainException deve ser lançada com mensagem contendo "maior que zero"

  Esquema do Cenário: Aplicar múltiplos lançamentos em sequência
    Quando eu aplico um <tipo> de <valor> no saldo do dia "2026-05-23"
    E eu aplico um <tipo2> de <valor2> no saldo do dia "2026-05-23"
    Então o saldo do dia "2026-05-23" deve ser <saldo_esperado>

    Exemplos:
      | tipo    | valor  | tipo2  | valor2 | saldo_esperado |
      | crédito | 500.00 | débito | 200.00 | 300.00         |
      | crédito | 100.00 | crédito | 50.00 | 150.00         |
      | débito  | 80.00  | débito | 20.00  | -100.00        |
