ARG OPENERP_BASE_IMAGE=openerp-base:local
FROM ${OPENERP_BASE_IMAGE}

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

WORKDIR /app
EXPOSE 8000
STOPSIGNAL SIGTERM
ENTRYPOINT ["/usr/local/bin/openerp-entrypoint"]
