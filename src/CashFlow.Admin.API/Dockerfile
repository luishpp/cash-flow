FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CashFlow.sln ./
COPY src/CashFlow.Admin.API/CashFlow.Admin.API.csproj src/CashFlow.Admin.API/
COPY src/CashFlow.Shared/CashFlow.Shared.csproj src/CashFlow.Shared/
COPY src/CashFlow.Transactions.API/CashFlow.Transactions.API.csproj src/CashFlow.Transactions.API/
COPY src/CashFlow.Balance.API/CashFlow.Balance.API.csproj src/CashFlow.Balance.API/
COPY src/CashFlow.Balance.Core/CashFlow.Balance.Core.csproj src/CashFlow.Balance.Core/
COPY src/CashFlow.Balance.Worker/CashFlow.Balance.Worker.csproj src/CashFlow.Balance.Worker/
COPY tests/CashFlow.UnitTests/CashFlow.UnitTests.csproj tests/CashFlow.UnitTests/
COPY tests/CashFlow.Architecture.Tests/CashFlow.Architecture.Tests.csproj tests/CashFlow.Architecture.Tests/

RUN dotnet restore src/CashFlow.Admin.API/CashFlow.Admin.API.csproj

COPY src/CashFlow.Admin.API/ src/CashFlow.Admin.API/
COPY src/CashFlow.Shared/ src/CashFlow.Shared/

RUN dotnet publish src/CashFlow.Admin.API/CashFlow.Admin.API.csproj \
    -c Release -o /app/publish --no-restore

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CashFlow.Admin.API.dll"]
