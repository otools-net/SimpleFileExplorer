# SFE as a container: an External server (opened by other people), its data in /data, its library in /library.
#
#   docker build -t sfe .
#   docker run -d --name sfe -p 8080:8080 -v sfe-data:/data -v /path/to/media:/library sfe
#   docker logs sfe        # the setup code, the first time
#
# The library folder and ffmpeg come from the environment (External mode: never from the web page). Everything else
# is in /data/appsettings.json (made on the first start) or in Sfe__… environment variables. The database, the caches
# (thumbnails, trickplay) and the logs can go elsewhere: SFE_DATABASE_DIR, SFE_CACHE_DIR, SFE_LOGS_DIR (e.g. /database,
# /cache and /logs, which the image prepares for volumes). See docs/docker.md.

# ---- The web client (Angular), built once whatever the target platform ----
FROM --platform=$BUILDPLATFORM node:24-bookworm-slim AS web
WORKDIR /src/src/Sfe.Web
COPY src/Sfe.Web/package.json src/Sfe.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/Sfe.Web/ ./
# Writes ../Sfe.Api/wwwroot (angular.json).
RUN npm run build

# ---- The server: the portable build (one self-contained executable) for the target platform ----
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS server
ARG TARGETARCH
WORKDIR /src
COPY Directory.Build.props Sfe.slnx CHANGELOG.md ./
COPY src/Sfe.Core/Sfe.Core.csproj src/Sfe.Core/
COPY src/Sfe.Api/Sfe.Api.csproj src/Sfe.Api/
RUN rid="linux-$( [ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64 )" \
    && dotnet restore src/Sfe.Api/Sfe.Api.csproj -r "$rid" -p:SfePortable=true
COPY build/portable/ build/portable/
COPY src/Sfe.Core/ src/Sfe.Core/
COPY src/Sfe.Api/ src/Sfe.Api/
COPY --from=web /src/src/Sfe.Api/wwwroot/ src/Sfe.Api/wwwroot/
RUN rid="linux-$( [ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64 )" \
    && dotnet publish src/Sfe.Api/Sfe.Api.csproj -c Release -r "$rid" --no-restore \
        -p:SfePortable=true -p:SfeSkipWebClient=true -o /app

# ---- The image: the program, ffmpeg, and nothing of the build ----
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*
COPY --from=server /app/ /app/
COPY build/docker/entrypoint.sh /app/entrypoint.sh
# A checkout with Windows line endings would break the script.
# The folders SFE writes belong to its user, so that a named volume mounted on one of them starts writable.
RUN sed -i 's/\r$//' /app/entrypoint.sh && chmod 755 /app/entrypoint.sh \
    && mkdir -p /data /database /cache /logs /library && chown "$APP_UID" /data /database /cache /logs

# Urls rather than the base image's ASPNETCORE_HTTP_PORTS: the appsettings.json of /data sets Urls, which would win.
ENV Sfe__Mode=External \
    ASPNETCORE_HTTP_PORTS= \
    Urls=http://+:8080 \
    Sfe__Root=/library \
    Sfe__Thumbnails__FfmpegPath=/usr/bin/ffmpeg \
    Sfe__OpenBrowser=false

USER $APP_UID
WORKDIR /data
VOLUME ["/data"]
EXPOSE 8080
ENTRYPOINT ["/app/entrypoint.sh"]
