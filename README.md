# VisionX Notifications Microservice

The standalone Notifications microservice is the central notification and push platform for the college application ecosystem.

## Architecture

- **Runtime**: ASP.NET Core 10, C# 13, Clean Architecture
- **Database**: PostgreSQL (`visionx_notifications`, default port 5435) with Entity Framework Core
- **Push Transport**: Firebase Cloud Messaging (FCM) via Firebase Admin SDK
- **Security**: RS256 JWT validation against Auth Service JWKS (`https://auth.visionx.io/.well-known/jwks.json`), HMAC-SHA256 internal event verification
- **Reliability**: Transactional outbox, `BackgroundService` delivery worker with exponential backoff retries, and scheduled campaign processor

## Ports & Hostnames

| Service | Container Port | Host Port | Production Domain |
|---|---|---|---|
| Notifications API | 8080 | 8010 | `https://notifications.visionx.io` |
| PostgreSQL DB | 5432 | 5435 | Localhost / Internal network |

## Running Locally

```bash
cd Notifications
docker compose up -d
```
API Documentation will be available at: `http://localhost:8010/swagger`
