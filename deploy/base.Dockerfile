FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:3a31a3c8c41f487ecab474cf6dcf152489fedca9fd145b0b49a4b9e616cd6dde AS aspnet
FROM redis:8@sha256:e2728231060693472a8efb488dd139a8142a01bd0612ae70429bf5b5be924be1 AS redis
FROM chrislusf/seaweedfs:4.53@sha256:972ba30c529dfc72a6a12ed427e94d99c7b6a6e59d97bfa52be0a849f43e2cc9 AS seaweedfs
FROM caddy:2@sha256:fc9cbc100585871d18d4e451cca7021785c90a7006087427ae5ceb3e4efe6424 AS caddy

FROM postgres:18@sha256:ec003ee60c23d320d1b81868f255a6fc724ac06d8f1ced4468532f7d8593f357 AS base
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
COPY --chmod=755 --from=caddy /usr/bin/caddy /usr/local/bin/caddy

RUN postgres --version \
    && dotnet --list-runtimes \
    && redis-server --version \
    && redis-cli --version \
    && weed version \
    && caddy version
