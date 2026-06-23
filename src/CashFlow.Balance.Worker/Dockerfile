FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Cache de restore: copia .sln + csproj antes do código
COPY CashFlow.sln ./
COPY src/CashFlow.Balance.Worker/CashFlow.Balance.Worker.csproj src/CashFlow.Balance.Worker/
COPY src/CashFlow.Balance.Core/CashFlow.Balance.Core.csproj src/CashFlow.Balance.Core/
COPY src/CashFlow.Shared/CashFlow.Shared.csproj src/CashFlow.Shared/
COPY src/CashFlow.Balance.API/CashFlow.Balance.API.csproj src/CashFlow.Balance.API/
COPY src/CashFlow.Transactions.API/CashFlow.Transactions.API.csproj src/CashFlow.Transactions.API/
COPY tests/CashFlow.UnitTests/CashFlow.UnitTests.csproj tests/CashFlow.UnitTests/
COPY tests/CashFlow.Architecture.Tests/CashFlow.Architecture.Tests.csproj tests/CashFlow.Architecture.Tests/

RUN dotnet restore src/CashFlow.Balance.Worker/CashFlow.Balance.Worker.csproj

# Copia o resto e publica
COPY src/CashFlow.Balance.Worker/ src/CashFlow.Balance.Worker/
COPY src/CashFlow.Balance.Core/ src/CashFlow.Balance.Core/
COPY src/CashFlow.Shared/ src/CashFlow.Shared/

RUN dotnet publish src/CashFlow.Balance.Worker/CashFlow.Balance.Worker.csproj \
    -c Release -o /app/publish --no-restore

# ---------- Runtime ----------
# Microsoft.Extensions.Hosting 10.0.0 puxa FrameworkReference de Microsoft.AspNetCore.App
# transitivamente — então não dá pra usar a imagem `runtime:10.0` mesmo sendo Worker Service.
# Usamos `aspnet:10.0`. Trade-off: +30MB na imagem vs +50 linhas de package suppressing.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "CashFlow.Balance.Worker.dll"]
