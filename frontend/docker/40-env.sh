#!/bin/sh
set -eu

: "${API_BASE_URL:=http://localhost:8080}"
: "${AUTH_AUTHORITY:=}"
: "${AUTH_CLIENT_ID:=}"
: "${AUTH_REDIRECT_URI:=}"
: "${AUTH_POST_LOGOUT_REDIRECT_URI:=}"
: "${AUTH_SCOPE:=openid profile email roles}"
: "${AUTH_DPOP:=true}"

# The := defaults above assign shell variables without exporting them, and
# envsubst only reads the environment — export so the defaults reach it.
export API_BASE_URL AUTH_AUTHORITY AUTH_CLIENT_ID AUTH_REDIRECT_URI \
  AUTH_POST_LOGOUT_REDIRECT_URI AUTH_SCOPE AUTH_DPOP

envsubst \
  '${API_BASE_URL} ${AUTH_AUTHORITY} ${AUTH_CLIENT_ID} ${AUTH_REDIRECT_URI} ${AUTH_POST_LOGOUT_REDIRECT_URI} ${AUTH_SCOPE} ${AUTH_DPOP}' \
  < /usr/share/nginx/html/env.template.js \
  > /usr/share/nginx/html/env.js
