# 与 .node-version 一致；该镜像无 corepack，显式安装仓库锁定的 pnpm。
FROM node:26 AS build
WORKDIR /app
RUN npm install --global pnpm@11.16.0
# pnpm-workspace.yaml 里有 allowBuilds、shamefullyHoist 等安装设置，缺了它依赖安装会失败。
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY . .
RUN pnpm run build

# 发布运行时：Caddy 托管静态产物并把 /api/* 代理到 API（外层可再套一层 Caddy 终止 TLS）。
FROM caddy:2 AS runtime
WORKDIR /srv
COPY --from=build /app/dist/ /srv/
COPY Caddyfile /etc/caddy/Caddyfile
