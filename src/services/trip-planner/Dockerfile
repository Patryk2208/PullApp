### Build context has to be TripPlanner solution root directory, so the docker build has to be called with
### docker build -f infra/docker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

WORKDIR /src/services/trip-planner/src
COPY services/trip-planner/src/TripPlanner.Api/TripPlanner.Api.csproj                       TripPlanner.Api/
COPY services/trip-planner/src/TripPlanner.Application/TripPlanner.Application.csproj       TripPlanner.Application/
COPY services/trip-planner/src/TripPlanner.Domain/TripPlanner.Domain.csproj                 TripPlanner.Domain/
COPY services/trip-planner/src/TripPlanner.Infrastructure/TripPlanner.Infrastructure.csproj TripPlanner.Infrastructure/

RUN dotnet restore TripPlanner.Api/TripPlanner.Api.csproj

COPY services/trip-planner/src .
COPY schemas/pullapp/core/v1/queue.proto /src/schemas/pullapp/core/v1/queue.proto 

WORKDIR /src/services/trip-planner/src/TripPlanner.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "TripPlanner.Api.dll"]