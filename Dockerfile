# syntax=docker/dockerfile:1.7

ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0.0
ARG REVISION=local

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION
ARG VERSION
WORKDIR /src

COPY ["Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj", "Somtoday2MicrosoftSDS/"]
RUN dotnet restore "Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj"

COPY . .
RUN dotnet publish "Somtoday2MicrosoftSDS/Somtoday2MicrosoftSDS.csproj" \
    --configuration "$BUILD_CONFIGURATION" \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false \
    -p:Version="$VERSION" \
    -p:AssemblyVersion="$VERSION" \
    -p:FileVersion="$VERSION" \
    -p:InformationalVersion="$VERSION" \
    -p:ContinuousIntegrationBuild=true

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
ARG VERSION
ARG REVISION

LABEL org.opencontainers.image.title="Somtoday2MicrosoftSDS" \
      org.opencontainers.image.description="Somtoday Connect to Microsoft School Data Sync CSV exporter" \
      org.opencontainers.image.source="https://github.com/Essella/Somtoday2MicrosoftSDS" \
      org.opencontainers.image.url="https://github.com/Essella/Somtoday2MicrosoftSDS" \
      org.opencontainers.image.documentation="https://github.com/Essella/Somtoday2MicrosoftSDS#readme" \
      org.opencontainers.image.licenses="AGPL-3.0-or-later" \
      org.opencontainers.image.version="$VERSION" \
      org.opencontainers.image.revision="$REVISION"

WORKDIR /app
COPY --from=build /app/publish .
COPY LICENSE /licenses/LICENSE
COPY THIRD-PARTY-NOTICES.md /licenses/THIRD-PARTY-NOTICES.md

USER 1654
ENTRYPOINT ["dotnet", "Somtoday2MicrosoftSDS.dll"]
