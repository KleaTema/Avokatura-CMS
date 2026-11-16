#!/bin/sh
# Task 052 — the frontend container's start: ONE image for every environment, configured here from the environment.
#   ASH_API_UPSTREAM   (required) the API, e.g. http://api:8080
#   ASH_ENVIRONMENT    Development | Uat | Production (default Production)
#   ASH_API_BASE_PATH  where the app calls the API ('' = same origin, the normal case)
#   ASH_IDP_HOSTS      the identity provider hosts the CSP allows, space separated (e.g. "ashprod.b2clogin.com");
#                      the SAME hosts the API's Auth:Providers point at — the app compares them at start and shows
#                      "Konfigurimi i hyrjes nuk përputhet me CSP" when they differ
#   ASH_MAX_UPLOAD_MB  the largest upload in MB (default 50); nginx allows it + 1 MB
#   ASH_HSTS           "on" (default) sends HSTS from here; "off" when TLS ends at AKSHI's ingress, which sends it
# Writes /tmp/ash/site.conf and /tmp/ash/config.json (the root filesystem stays read-only), then runs nginx.
set -eu

: "${ASH_API_UPSTREAM:?ASH_API_UPSTREAM is required (e.g. http://api:8080)}"
ASH_ENVIRONMENT="${ASH_ENVIRONMENT:-Production}"
ASH_API_BASE_PATH="${ASH_API_BASE_PATH:-}"
ASH_IDP_HOSTS="${ASH_IDP_HOSTS:-}"
ASH_MAX_UPLOAD_MB="${ASH_MAX_UPLOAD_MB:-50}"
ASH_VERSION="$(cat /etc/ash/version 2>/dev/null || echo unknown)"

case "$ASH_MAX_UPLOAD_MB" in
  ''|*[!0-9]*) echo "ASH_MAX_UPLOAD_MB must be a whole number of megabytes" >&2; exit 1 ;;
esac

# the provider origins (https://host) and the JSON list of hosts for /config.json; hosts only, no wildcards
origins=""
hosts_json=""
for host in $ASH_IDP_HOSTS; do
  case "$host" in
    *[!A-Za-z0-9.-]*|'') echo "ASH_IDP_HOSTS: \"$host\" is not a host name" >&2; exit 1 ;;
  esac
  origins="$origins https://$host"
  hosts_json="$hosts_json${hosts_json:+,}\"$host\""
done

# the CSP: the template made by tools/security/csp.mjs at build time, the provider origins filled in
ASH_CSP="$(sed "s# __IDP_ORIGINS__#${origins}#g" /etc/ash/csp.template)"
export ASH_CSP ASH_API_UPSTREAM
export ASH_MAX_BODY="$((ASH_MAX_UPLOAD_MB + 1))m"
if [ "${ASH_HSTS:-on}" = "on" ]; then
  export ASH_HSTS="max-age=31536000; includeSubDomains"
else
  export ASH_HSTS=""
fi

mkdir -p /tmp/ash
# only these variables: nginx's own $host, $uri … stay as they are
envsubst '${ASH_API_UPSTREAM} ${ASH_CSP} ${ASH_MAX_BODY} ${ASH_HSTS}' < /etc/ash/site.conf.template > /tmp/ash/site.conf

cat > /tmp/ash/config.json <<EOF
{
  "apiBasePath": "${ASH_API_BASE_PATH}",
  "environment": "${ASH_ENVIRONMENT}",
  "version": "${ASH_VERSION}",
  "cspHosts": [${hosts_json}]
}
EOF

echo "ash-frontend ${ASH_VERSION} (${ASH_ENVIRONMENT}): API ${ASH_API_UPSTREAM}, identity hosts [${ASH_IDP_HOSTS}]"
exec nginx -c /etc/nginx/nginx.conf -g 'daemon off;'
