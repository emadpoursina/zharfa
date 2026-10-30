# Zharfa web image — builds the shared Persian RTL frontend, serves it behind
# nginx, which is also the Demo 1 reverse proxy for /api.
# Build context is the repo root:  docker build -f deploy/web/Dockerfile .
FROM node:24-alpine AS build
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json* ./
RUN npm install --no-audit --no-fund
COPY frontend/ .
RUN npm run build

FROM nginx:1.30-alpine AS runtime
COPY deploy/web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
