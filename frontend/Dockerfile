FROM node:25-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html

COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY docker/40-env.sh /docker-entrypoint.d/40-env.sh
COPY docker/env.template.js /usr/share/nginx/html/env.template.js
COPY --from=build /app/dist/ /usr/share/nginx/html/

RUN chmod +x /docker-entrypoint.d/40-env.sh

EXPOSE 80
