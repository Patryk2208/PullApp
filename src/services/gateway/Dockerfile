FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_MODE=Release
WORKDIR /src
COPY Gateway.csproj .
RUN dotnet restore Gateway.csproj
COPY . .
RUN dotnet build Gateway.csproj -c $BUILD_MODE -o /app/build

FROM build AS publish
ARG BUILD_MODE=Release
RUN dotnet publish Gateway.csproj -c $BUILD_MODE -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Gateway.dll"]
