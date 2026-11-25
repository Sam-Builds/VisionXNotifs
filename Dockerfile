FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy project definitions for restore caching
COPY src/Notifications.Domain/Notifications.Domain.csproj src/Notifications.Domain/
COPY src/Notifications.Application/Notifications.Application.csproj src/Notifications.Application/
COPY src/Notifications.Infrastructure/Notifications.Infrastructure.csproj src/Notifications.Infrastructure/
COPY src/Notifications.Api/Notifications.Api.csproj src/Notifications.Api/

RUN dotnet restore src/Notifications.Api/Notifications.Api.csproj

# Copy all source files
COPY src/ src/

WORKDIR /app/src/Notifications.Api
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Notifications.Api.dll"]
