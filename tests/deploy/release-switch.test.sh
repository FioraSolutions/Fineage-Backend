#!/usr/bin/env bash
# Local proof of deploy/release-switch.sh (#989): no host, no systemd, no network beyond loopback.
# A fake "service" serves /health/ready from the release `current` points at: 200 when that release's
# zone/HEALTH says ok, 503 otherwise. The fake Fineage.Login answers `migrate --plan` from login/PENDING.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SWITCH="$HERE/../../deploy/release-switch.sh"
WORK="$(mktemp -d)"
SERVER_PID=""
cleanup() {
  [[ -n "$SERVER_PID" ]] && kill "$SERVER_PID" 2>/dev/null || true
  rm -rf "$WORK"
}
trap cleanup EXIT

PORT="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')"
APP="$WORK/app"
mkdir -p "$APP"

cat > "$WORK/server.py" <<'PY'
import http.server, os, sys
app, port = sys.argv[1], int(sys.argv[2])
class H(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        try:
            state = open(os.path.join(app, "current", "zone", "HEALTH")).read().strip()
        except OSError as e:
            state = "missing: %s" % e
        ok = self.path == "/health/ready" and state == "ok"
        body = ('{"ok":%s,"checks":[{"name":"world","ok":%s,"detail":"%s"}]}' % (str(ok).lower(), str(ok).lower(), state)).encode()
        self.send_response(200 if ok else 503)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)
    def log_message(self, *a):
        pass
http.server.ThreadingHTTPServer(("127.0.0.1", port), H).serve_forever()
PY
python3 "$WORK/server.py" "$APP" "$PORT" &
SERVER_PID=$!
for _ in $(seq 1 50); do
  curl -s -o /dev/null "http://127.0.0.1:${PORT}/" && break
  sleep 0.1
done

make_release() { # dir health pending
  mkdir -p "$1/login" "$1/zone"
  printf '#!/usr/bin/env bash\necho "migrations: applied=7 pending=$(cat "$(dirname "$0")/PENDING") unverified=0"\n' > "$1/login/Fineage.Login"
  printf '#!/usr/bin/env bash\nexit 0\n' > "$1/zone/Fineage.Zone"
  chmod +x "$1/login/Fineage.Login" "$1/zone/Fineage.Zone"
  echo "$2" > "$1/zone/HEALTH"
  echo "$3" > "$1/login/PENDING"
}

export FINEAGE_RESTART=command
export FINEAGE_RESTART_COMMAND="echo restarted >> '$WORK/restarts'"
export FINEAGE_READY_URLS="http://127.0.0.1:${PORT}/health/ready"
export FINEAGE_READY_TCP_PORTS="$PORT"
export FINEAGE_READY_TIMEOUT=4
export FINEAGE_KEEP_RELEASES=3

pass=0
check() { if eval "$2"; then echo "ok   - $1"; pass=$((pass + 1)); else echo "FAIL - $1"; exit 1; fi; }

# 1. Old in-place layout is adopted as the first release, then a good release goes live.
make_release "$APP" ok 0           # login/ and zone/ as real directories, like before #989
make_release "$APP/releases/r1.partial" ok 0
"$SWITCH" "$APP" r1 > "$WORK/out1" 2>&1
check "good release goes live" '[[ "$(readlink "$APP/current")" == releases/r1 ]]'
check "legacy layout adopted" 'ls "$APP/releases" | grep -q "^legacy-"'
check "login/zone are symlinks into current" '[[ "$(readlink "$APP/login")" == current/login && "$(readlink "$APP/zone")" == current/zone ]]'
check "deploy reports OK" 'grep -q "Deploy OK" "$WORK/out1"'

# 2. A broken release is rolled back atomically to r1, and the deploy fails.
make_release "$APP/releases/r2.partial" broken 0
if "$SWITCH" "$APP" r2 > "$WORK/out2" 2>&1; then echo "FAIL - broken release reported success"; exit 1; fi
check "broken release rolled back to previous" '[[ "$(readlink "$APP/current")" == releases/r1 ]]'
check "rollback names the readiness failure" 'grep -q "not ready after 4s" "$WORK/out2" && grep -q "ROLLED BACK: production is on r1" "$WORK/out2"'
check "failed release kept for inspection" '[[ -d "$APP/releases/r2" ]]'

# 3. A broken release that applied forward-only migrations is not rolled back by binaries.
make_release "$APP/releases/r3.partial" broken 2
if "$SWITCH" "$APP" r3 > "$WORK/out3" 2>&1; then echo "FAIL - broken migrating release reported success"; exit 1; fi
check "rollback refused after migrations" 'grep -q "rollback refused: r3 applied 2 forward-only migration" "$WORK/out3"'
check "current left on the migrating release" '[[ "$(readlink "$APP/current")" == releases/r3 ]]'

# 4. Recovery deploy, then pruning keeps current, previous and KEEP-2 others.
make_release "$APP/releases/r4.partial" ok 0
"$SWITCH" "$APP" r4 > "$WORK/out4" 2>&1
check "recovery release live" '[[ "$(readlink "$APP/current")" == releases/r4 ]]'
check "old releases pruned to the limit" '[[ "$(find "$APP/releases" -mindepth 1 -maxdepth 1 -type d | wc -l)" -le 3 ]]'
check "previous release never pruned" '[[ -d "$APP/releases/r3" ]]'

# 5. A failed preflight never touches current.
mkdir -p "$APP/releases/r5.partial/login" "$APP/releases/r5.partial/zone"
printf '#!/usr/bin/env bash\necho "migration tamper: 0042 checksum" >&2\nexit 1\n' > "$APP/releases/r5.partial/login/Fineage.Login"
printf '#!/usr/bin/env bash\nexit 0\n' > "$APP/releases/r5.partial/zone/Fineage.Zone"
chmod +x "$APP/releases/r5.partial/login/Fineage.Login" "$APP/releases/r5.partial/zone/Fineage.Zone"
if "$SWITCH" "$APP" r5 > "$WORK/out5" 2>&1; then echo "FAIL - failed preflight reported success"; exit 1; fi
check "preflight failure leaves current alone" '[[ "$(readlink "$APP/current")" == releases/r4 ]] && grep -q "was not touched" "$WORK/out5"'

echo "release-switch: ${pass} checks passed"
