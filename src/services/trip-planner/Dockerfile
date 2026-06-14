FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/TripPlanner.Api/TripPlanner.Api.csproj TripPlanner.Api/
COPY src/TripPlanner.Application/TripPlanner.Application.csproj TripPlanner.Application/
COPY src/TripPlanner.Domain/TripPlanner.Domain.csproj TripPlanner.Domain/
COPY src/TripPlanner.Infrastructure/TripPlanner.Infrastructure.csproj TripPlanner.Infrastructure/

RUN dotnet restore TripPlanner.Api/TripPlanner.Api.csproj

COPY src/. .
COPY schemas/. /app/protos

WORKDIR /src/TripPlanner.Api
RUN dotnet publish -p:ContainerBuild=true -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "TripPlanner.Api.dll"]