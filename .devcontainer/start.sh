#!/usr/bin/env bash
# Starts AI Friendly Task Manager inside a devcontainer or Codespace.
#
# A script rather than a one-liner in devcontainer.json because there are three things to get
# right, and each was learned from it going wrong:
#
#   1. Start only if nothing is answering. postAttachCommand runs on every attach, and a browser
#      reconnecting counts — the second run bound a port the first was holding and died with a page
#      of stack trace, which is what a visitor saw.
#   2. Ask the port, not the process list. This script's own command line contains the project
#      name, so a pgrep would match itself and never start anything.
#   3. --urls on the command line, not ASPNETCORE_URLS in the environment. `dotnet run` applies
#      launchSettings.json, whose applicationUrl is http://localhost:5249, and that overrides the
#      ambient variable — leaving the app bound to loopback with nothing for the forwarder to see.
set -euo pipefail

PORT=5249

if curl -sf -o /dev/null --max-time 2 "http://localhost:$PORT/api/board"; then
  echo "AI Friendly Task Manager is already running on port $PORT."
  exit 0
fi

# The forwarded address, when there is one. Printed before the app starts so it is visible even if
# the preview pane never opens — a visitor should never have to know what a Ports tab is.
if [[ -n "${CODESPACE_NAME:-}" && -n "${GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN:-}" ]]; then
  echo ""
  echo "  AI Friendly Task Manager will be at:"
  echo "  https://${CODESPACE_NAME}-${PORT}.${GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN}"
  echo ""
fi

exec dotnet run --project src/AIFTM.Api --urls "http://0.0.0.0:$PORT"
