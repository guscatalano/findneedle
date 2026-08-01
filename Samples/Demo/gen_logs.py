#!/usr/bin/env python3
"""
Generate the synthetic demo logs used for screenshots and manual testing.

Deterministic (fixed seed) so re-running produces byte-identical files — that's why the
generated logs are committed alongside this script: the screenshots in docs/screenshots/
stay reproducible. Everything here is FAKE (made-up services, users, IPs); no real data.

Usage:  python gen_logs.py        # writes ./logs/*.log next to this script
Load the ./logs folder in FindNeedle to reproduce the result-viewer screenshots.
"""
import random, datetime, os

random.seed(42)
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "logs")
os.makedirs(OUT, exist_ok=True)

start = datetime.datetime(2026, 8, 1, 9, 15, 0)
levels = ["INFO", "INFO", "INFO", "INFO", "DEBUG", "DEBUG", "WARN", "WARN", "ERROR"]
services = {
    "AuthService":    ["issued token for user {u}", "token refresh for {u}", "MFA challenge sent to {u}", "login succeeded user={u} ip={ip}", "login FAILED user={u} ip={ip} reason=bad_password", "session {sid} expired", "validating bearer token", "revoked session {sid}"],
    "NetworkManager": ["connection established to {ip}:{port}", "TLS handshake complete peer={ip} cipher=TLS_AES_256_GCM", "socket read {n} bytes from {ip}", "connection reset by peer {ip}", "retrying connection to {ip}:{port} attempt {a}", "connection TIMEOUT to {ip}:{port} after 30000ms", "closing idle connection {ip}", "DNS resolve svc.internal -> {ip}"],
    "CacheLayer":     ["cache HIT key=user:{u}", "cache MISS key=session:{sid}", "evicted {n} entries (LRU)", "cache warm complete {n} keys", "write-through key=order:{oid}", "cache stampede detected key=config:global", "TTL expired key=token:{sid}"],
    "RequestHandler": ["GET /api/v2/orders/{oid} 200 {ms}ms", "POST /api/v2/checkout 201 {ms}ms user={u}", "GET /api/v2/orders/{oid} 404 {ms}ms", "request queued depth={n}", "POST /api/v2/checkout 500 {ms}ms user={u} err=DbTimeout", "rate limit applied user={u} 429", "handling request rid={rid}"],
    "DbPool":         ["acquired connection pool=primary size={n}/32", "query took {ms}ms rows={n}", "connection pool EXHAUSTED waiting...", "slow query WARN {ms}ms SELECT orders", "released connection pool=primary", "transaction {rid} committed", "deadlock retry {rid} attempt {a}", "query TIMEOUT after 15000ms rid={rid}"],
    "TlsHandshake":   ["ClientHello received peer={ip}", "cert chain validated CN=svc.internal", "handshake FAILED peer={ip} alert=bad_certificate", "resumed session ticket peer={ip}", "negotiated ALPN=h2 peer={ip}"],
}


def val(m):
    return m.format(u=random.choice(["alice", "bob", "carol", "dvega", "mchen", "root", "svc_batch", "kpatel"]),
                    ip=f"10.0.{random.randint(0,9)}.{random.randint(2,240)}", port=random.choice([443, 8443, 5671, 1433, 6379]),
                    n=random.randint(1, 4096), a=random.randint(1, 5), ms=random.randint(2, 3200),
                    sid=f"{random.randint(0x100000,0xffffff):06x}", oid=random.randint(10000, 99999),
                    rid=f"{random.randint(0x1000,0xffff):04x}")


def gen(fname, svc_list, count):
    t = start + datetime.timedelta(seconds=random.randint(0, 60))
    with open(os.path.join(OUT, fname), "w") as f:
        for _ in range(count):
            t += datetime.timedelta(milliseconds=random.randint(3, 900))
            svc = random.choice(svc_list)
            msg = val(random.choice(services[svc]))
            lvl = "ERROR" if ("FAILED" in msg or "TIMEOUT" in msg or "500" in msg or "EXHAUSTED" in msg or "reset" in msg or "deadlock" in msg) else random.choice(levels)
            pid = 0x1A40 + list(services).index(svc) * 3  # deterministic per service (not hash(), which is seeded)
            tid = 0x2000 + random.randint(0, 0x1fff)
            f.write(f"{t.strftime('%Y-%m-%dT%H:%M:%S.%f')[:-3]}Z  {lvl:5}  [{pid:04X}.{tid:04X}]  {svc:15} {msg}\n")


gen("auth-frontend.log",  ["AuthService", "NetworkManager", "RequestHandler", "TlsHandshake"], 4200)
gen("orders-backend.log", ["RequestHandler", "DbPool", "CacheLayer", "NetworkManager"], 5200)
gen("edge-proxy.log",     ["NetworkManager", "TlsHandshake", "CacheLayer"], 3100)
print("wrote logs to", OUT)
