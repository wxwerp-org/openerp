# 所有阶段均固定内容摘要；构建工具不进入最终镜像。
FROM --platform=$BUILDPLATFORM node:26.13.0-alpine3.23@sha256:44a3247313f9bc069a4ec25d01b66254ed7dc4d0408727d8720dc827f0753423 AS web
WORKDIR /src/OpenERP.Web
RUN npm install --global pnpm@11.16.0
COPY OpenERP.Web/package.json OpenERP.Web/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY OpenERP.Web/ ./
RUN pnpm typecheck && pnpm build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.404-alpine3.24@sha256:0294f285d062afacc8b5f6d7febb0cd4ee879320e5585fa15f2f4ebf1feb9ba7 AS api
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY OpenERP.Api/ OpenERP.Api/
COPY OpenERP.ServiceDefaults/ OpenERP.ServiceDefaults/
RUN dotnet restore OpenERP.Api/OpenERP.Api.csproj --locked-mode \
    && dotnet publish OpenERP.Api/OpenERP.Api.csproj --no-restore -c Release -o /out/app /p:UseAppHost=false \
    && dotnet tool install dotnet-ef --version 10.0.15 --tool-path /tools \
    && /tools/dotnet-ef migrations bundle --project OpenERP.Api/OpenERP.Api.csproj --configuration Release \
       --target-runtime linux-musl-x64 --output /out/efbundle

FROM redis:8.10.3-alpine3.23@sha256:7958cfbb9d152f87dadfbc89f1b0b15938f0b81ff8982feb327476f4911935c3 AS redis
FROM chrislusf/seaweedfs:4.53@sha256:ccad23e18639c8b98cee17e7100b95830a06077b56d8e5b006d6dcf8ec5e94a0 AS seaweedfs

FROM postgres:18.7-alpine3.23@sha256:60aac580d8dbe3093c097bb1d11f9e62999814d3a1da2c1aaf729c0b7db117f2 AS runtime
ARG TARGETARCH
RUN test "$TARGETARCH" = amd64

# 只拷贝 Redis 运行文件，不能覆盖 PostgreSQL 的同名 entrypoint。
COPY --from=redis /usr/local/bin/redis-* /usr/local/bin/
COPY --from=redis /usr/local/lib/ /usr/local/lib/
# 现有应用使用 Go server（Master/Volume/Filer/S3），不携带未使用的 Rust 服务。
COPY --from=seaweedfs /usr/bin/weed /usr/bin/weed
RUN apk add --no-cache coreutils sed curl xz libstdc++ libgcc libssl3 libcrypto3 icu-data-full tzdata \
    && apk add --no-cache --virtual .scan-deps pax-utils \
    && scanelf --needed --nobanner --quiet --format '%n#p' --recursive /usr/local/bin/redis-* /usr/local/lib/redis \
       | awk '{ print $1 }' | tr ',' '\n' | sort -u | sed '/^$/d; s/^/so:/' > /tmp/runtime-dependencies \
    && apk add --no-cache $(cat /tmp/runtime-dependencies) \
    && apk del .scan-deps \
    && rm /tmp/runtime-dependencies

# 官方 linux-musl-x64 ASP.NET Core 运行时，包含 .NET host；不启用 Native AOT。
RUN mkdir -p /usr/share/dotnet \
    && curl -fsSL https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.15/aspnetcore-runtime-10.0.15-linux-musl-x64.tar.gz -o /tmp/dotnet.tar.gz \
    && printf '%s  %s\n' '0c4608259c99236bcb860c145b8794c72c6817d01d4ed0ad2132b78cd90e13c6527341edb787b38d9e0382d806b61bfbb170b9951cec68d1043840ff176fa90b' /tmp/dotnet.tar.gz | sha512sum -c - \
    && tar -xzf /tmp/dotnet.tar.gz -C /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
    && rm /tmp/dotnet.tar.gz

RUN curl -fsSL https://github.com/just-containers/s6-overlay/releases/download/v3.2.3.2/s6-overlay-noarch.tar.xz -o /tmp/s6-noarch.tar.xz \
    && curl -fsSL https://github.com/just-containers/s6-overlay/releases/download/v3.2.3.2/s6-overlay-x86_64.tar.xz -o /tmp/s6-x86_64.tar.xz \
    && printf '%s  %s\n' '5379750ed30a84bbd2e2dd74847ba6b5bd29cd0b2e3ea2ec58049b57eb2eda12' /tmp/s6-noarch.tar.xz | sha256sum -c - \
    && printf '%s  %s\n' 'e6befcc96a437a3831386ecfc51808c5d3e939dc5fe3c02ae9284599e8aa2408' /tmp/s6-x86_64.tar.xz | sha256sum -c - \
    && tar -C / -Jxpf /tmp/s6-noarch.tar.xz \
    && tar -C / -Jxpf /tmp/s6-x86_64.tar.xz \
    && rm /tmp/s6-*.tar.xz \
    && addgroup -S -g 1655 redis && adduser -S -D -H -u 1655 -G redis redis \
    && addgroup -S -g 1656 seaweed && adduser -S -D -H -u 1656 -G seaweed seaweed \
    && addgroup -S -g 1654 app && adduser -S -D -H -u 1654 -G app app

COPY --from=api /out/app/ /app/
COPY --from=api --chmod=755 /out/efbundle /app/efbundle
COPY --from=web /src/OpenERP.Web/dist/ /app/wwwroot/
COPY deploy/container/rootfs/ /
RUN find /etc/s6-overlay/s6-rc.d -name run -o -name finish | xargs chmod 755 \
    && chmod 755 /usr/local/libexec/openerp/* \
    && dotnet --list-runtimes && postgres --version && redis-server --version && weed version

ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/openerp-bundle \
    DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8081 \
    ASPNETCORE_CONTENTROOT=/app \
    OPENERP_RUNTIME_CONFIG=/run/openerp/appsettings.json \
    S6_BEHAVIOUR_IF_STAGE2_FAILS=2 \
    S6_LOGGING=0
WORKDIR /app
EXPOSE 8081
VOLUME ["/var/lib/postgresql", "/var/lib/redis", "/data"]
HEALTHCHECK --interval=15s --timeout=10s --start-period=180s --retries=3 \
    CMD curl --fail --silent --max-time 8 -H "Host: ${AllowedHosts%%;*}" http://127.0.0.1:8081/health >/dev/null || exit 1
STOPSIGNAL SIGTERM
ENTRYPOINT ["/init"]
CMD []
