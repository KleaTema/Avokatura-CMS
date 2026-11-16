# Task 052 — the frontend image: the built SPA served by nginx (unprivileged image: non-root, port 8080), ONE image
# for every environment — the values come from the environment at start (deploy/frontend/docker-entrypoint.sh writes
# /config.json and the site configuration with the CSP). Build from the repository root:
#   docker build -f deploy/docker/frontend.Dockerfile --build-arg VERSION=1.2.3 -t ash-frontend:1.2.3 .
#   docker run -e ASH_API_UPSTREAM=http://api:8080 -e ASH_IDP_HOSTS=ashprod.b2clogin.com --read-only --tmpfs /tmp ash-frontend:1.2.3

FROM node:24.21.0-bookworm-slim@sha256:d6aa754f16b3197301076f047b5def2f02ea1dbbc2ca920407d46d7ec7f87b20 AS build
WORKDIR /src
COPY tools/security tools/security
COPY src/frontend/package.json src/frontend/package-lock.json src/frontend/
RUN cd src/frontend && npm ci --no-audit --no-fund
COPY src/frontend src/frontend
ARG VERSION=0.0.0-local
RUN cd src/frontend && npm run build
# the CSP template, from the ONE definition (tools/security/csp.mjs); the entrypoint fills in the provider origins
RUN node --input-type=module -e "import { buildCsp } from './tools/security/csp.mjs'; console.log(buildCsp(['__IDP_ORIGINS__']))" > /src/csp.template \
    && echo "${VERSION}" > /src/version

FROM nginxinc/nginx-unprivileged:1.29.3-alpine@sha256:5aea7cc516b419e3526f47dd1531be31a56a046cfe44754d94f9383e13e2ee99 AS runtime
ARG VERSION=0.0.0-local
LABEL org.opencontainers.image.title="ash-frontend" org.opencontainers.image.version="${VERSION}"
COPY --from=build /src/src/frontend/dist /usr/share/nginx/html
# the dev defaults of public/config.json never ship: /config.json is generated at start
RUN rm -f /usr/share/nginx/html/config.json
COPY deploy/frontend/nginx.conf /etc/nginx/nginx.conf
COPY deploy/frontend/site.conf.template /etc/ash/site.conf.template
COPY --from=build /src/csp.template /src/version /etc/ash/
COPY --chmod=0755 deploy/frontend/docker-entrypoint.sh /usr/local/bin/ash-entrypoint.sh
USER 101
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1
ENTRYPOINT ["/usr/local/bin/ash-entrypoint.sh"]
