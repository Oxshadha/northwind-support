# syntax=docker/dockerfile:1
#
# The Support Desk API. Build from the repository root:
#   docker build -f apps/api/Dockerfile -t supportdesk-api .
#
# Nothing environment-specific is baked in: the connection string, the JWT signing key and the
# environment name are all supplied when the container runs (see docker-compose.yml).

# --- Build: the .NET 10 SDK restores and publishes ------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the project files and the build settings alone. This layer only changes
# when a dependency does, so editing a .cs file reuses it instead of downloading packages again.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY apps/api/src/SupportDesk.Domain/SupportDesk.Domain.csproj apps/api/src/SupportDesk.Domain/
COPY apps/api/src/SupportDesk.Application/SupportDesk.Application.csproj apps/api/src/SupportDesk.Application/
COPY apps/api/src/SupportDesk.Infrastructure/SupportDesk.Infrastructure.csproj apps/api/src/SupportDesk.Infrastructure/
COPY apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj apps/api/src/SupportDesk.Presentation/
RUN dotnet restore apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj

# Then the source, and a publish that reuses the restore above.
COPY .editorconfig ./
COPY apps/api/src/ apps/api/src/
RUN dotnet publish apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

# --- Run: the ASP.NET Core runtime only, no SDK, no source -----------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Plain HTTP inside the container; TLS belongs to whatever sits in front of it.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish ./

# The image's built-in unprivileged user ("app"), so a compromise of the process is not root.
USER $APP_UID

ENTRYPOINT ["dotnet", "SupportDesk.Presentation.dll"]
