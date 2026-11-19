FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS aspnet
FROM redis:8 AS redis
FROM chrislusf/seaweedfs:4.53 AS seaweedfs

FROM postgres:18
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends ca-certificates curl \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --uid 1654 --user-group --no-log-init --shell /usr/sbin/nologin --create-home openerp-api \
    && useradd --uid 1655 --user-group --no-log-init --shell /usr/sbin/nologin openerp-redis \
    && useradd --uid 1656 --user-group --no-log-init --shell /usr/sbin/nologin openerp-weed \
    && useradd --uid 1657 --user-group --no-log-init --shell /usr/sbin/nologin --create-home openerp-web \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet

COPY --from=aspnet /usr/share/dotnet/ /usr/share/dotnet/
COPY --from=redis /usr/local/bin/redis-server /usr/local/bin/redis-cli /usr/local/bin/
COPY --from=seaweedfs /usr/bin/weed /usr/local/bin/
COPY --chmod=755 caddy /usr/local/bin/
COPY api/ /app/
COPY --chmod=755 efbundle /app/efbundle
COPY web/ /srv/
COPY Caddyfile /etc/caddy/Caddyfile
COPY --chmod=755 entrypoint.sh /usr/local/bin/openerp-entrypoint

# 同时提供给入口脚本、迁移和 docker compose exec 启动的管理员 CLI。
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ASPNETCORE_URLS=http://127.0.0.1:8080 \
    POSTGRES_INITDB_ARGS="--auth-host=scram-sha-256 --auth-local=scram-sha-256" \
    POSTGRES_USER=postgres \
    POSTGRES_DB=openerp \
    POSTGRES_PASSWORD_FILE=/var/lib/openerp/credentials/postgres/current/password \
    ConnectionStrings__openerp="Host=127.0.0.1;Port=5432;Database=openerp;Username=postgres" \
    PGPASSFILE=/var/lib/openerp/credentials/postgres/current/pgpass \
    ConnectionStrings__redis=127.0.0.1:6379 \
    ConnectionStrings__seaweedfs=Endpoint=http://127.0.0.1:8333 \
    PORT=8000 \
    API_HTTP=http://127.0.0.1:8080

RUN dotnet --list-runtimes && redis-server --version && redis-cli --version && weed version && caddy version

WORKDIR /app
EXPOSE 8000
STOPSIGNAL SIGTERM
ENTRYPOINT ["/usr/local/bin/openerp-entrypoint"]
