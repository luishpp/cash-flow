# language: pt-BR
@e2e @auth @bdd
Funcionalidade: Autenticação JWT, refresh tokens e lockout (E2E)
  Como sistema CashFlow
  Quero proteger o login contra brute-force (lockout) e suportar sessões longas (refresh)
  Para que apenas usuários autenticados registrem lançamentos com UX e segurança razoáveis

  Cenário: Login com credenciais válidas emite access + refresh token
    Quando faço POST em "/api/v1/auth/login" com credenciais demo válidas
    Então a resposta deve ser 200
    E a resposta deve conter um access token não vazio
    E a resposta deve conter um refresh token não vazio
    E a role retornada deve ser "Merchant"

  Cenário: Login com senha errada retorna 401
    Quando faço POST em "/api/v1/auth/login" com username "carlos" e password "senha-errada"
    Então a resposta deve ser 401

  Cenário: Login com usuário inexistente retorna 401
    Quando faço POST em "/api/v1/auth/login" com username "fulano-inexistente" e password "qualquer"
    Então a resposta deve ser 401

  Cenário: POST de transação sem token retorna 401
    Quando faço POST em "/api/v1/transactions" sem token com payload de crédito de 100.00
    Então a resposta deve ser 401

  Cenário: POST de transação com token válido retorna 201
    Dado que estou autenticado com credenciais demo válidas
    Quando faço POST em "/api/v1/transactions" com token e payload de crédito de 150.00
    Então a resposta deve ser 201
    E o body deve conter um id de transação válido

  @lockout
  Cenário: Conta é travada após 3 tentativas falhas consecutivas
    Quando faço 3 tentativas de login com password "senha-errada"
    E faço POST em "/api/v1/auth/login" com credenciais demo válidas
    Então a resposta deve ser 401

  @refresh
  Cenário: Refresh rotaciona o token e emite novo par
    Dado que estou autenticado com credenciais demo válidas
    Quando faço POST em "/api/v1/auth/refresh" com o refresh token atual
    Então a resposta deve ser 200
    E a resposta deve conter um access token não vazio
    E a resposta deve conter um refresh token não vazio
    E o novo refresh token deve ser diferente do anterior

  @refresh
  Cenário: Refresh token revogado após uso (rotação) não pode ser reutilizado
    Dado que estou autenticado com credenciais demo válidas
    E rotaciono o refresh token uma vez
    Quando faço POST em "/api/v1/auth/refresh" com o refresh token anterior
    Então a resposta deve ser 401

  @refresh
  Cenário: Logout revoga o refresh token
    Dado que estou autenticado com credenciais demo válidas
    Quando faço POST em "/api/v1/auth/logout" com o refresh token atual
    Então a resposta deve ser 204
    Quando faço POST em "/api/v1/auth/refresh" com o refresh token atual
    Então a resposta deve ser 401
