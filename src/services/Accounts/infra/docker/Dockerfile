### docker build -f infra/docker/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Accounts.Api/Accounts.Api.csproj                       Accounts.Api/
COPY src/Accounts.Application/Accounts.Application.csproj       Accounts.Application/
COPY src/Accounts.Domain/Accounts.Domain.csproj                 Accounts.Domain/
COPY src/Accounts.Infrastructure/Accounts.Infrastructure.csproj Accounts.Infrastructure/

RUN dotnet restore Accounts.Api/Accounts.Api.csproj

COPY src/. .

WORKDIR /src/Accounts.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Accounts.Api.dll"]
