### docker build -f infra/docker/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_MODE=Release

WORKDIR /src
COPY src/PullApp.Accounts.Api/PullApp.Accounts.Api.csproj                       PullApp.Accounts.Api/
COPY src/PullApp.Accounts.Application/PullApp.Accounts.Application.csproj       PullApp.Accounts.Application/
COPY src/PullApp.Accounts.Domain/PullApp.Accounts.Domain.csproj                 PullApp.Accounts.Domain/
COPY src/PullApp.Accounts.Infrastructure/PullApp.Accounts.Infrastructure.csproj PullApp.Accounts.Infrastructure/
RUN dotnet restore PullApp.Accounts.Api/PullApp.Accounts.Api.csproj

COPY src/. .
WORKDIR /src/PullApp.Accounts.Api
RUN dotnet publish -c $BUILD_MODE -o /app/publish


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "PullApp.Accounts.Api.dll"]
