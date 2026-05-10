"""
Generate a large synthetic log file in the same format as
FindNeedleRuleDSL/Examples/sample.log for stress-testing the result viewers.

Format per line:
    [YYYY-MM-DD HH:MM:SS] LEVEL: message text

Usage:
    python generate_large_sample.py [--lines N] [--out path]

Defaults: 500_000 lines, ./Samples/large-sample.log
"""

import argparse
import datetime
import os
import random


SERVICES = [
    "AuthService", "PaymentGateway", "OrderService", "InventoryService",
    "NotificationService", "BillingService", "Cache", "Database",
    "Scheduler", "MetricsCollector", "EmailService", "SmsService",
    "WebhookDispatcher", "FileUploader", "ImageProcessor", "SearchIndexer",
    "AuditLog", "FeatureFlags", "RateLimiter", "WebServer",
]

USERS = [
    "alice", "bob", "carol", "dave", "eve", "frank", "grace", "heidi",
    "ivan", "judy", "ken", "liam", "mallory", "nina", "oscar", "peggy",
    "quentin", "rita", "sam", "trudy", "ursula", "victor", "wendy", "xavier",
]

HOSTS = [
    "api-1.prod", "api-2.prod", "api-3.prod", "db-primary.prod",
    "db-replica.prod", "cache-1.prod", "cache-2.prod", "queue-1.prod",
    "queue-2.prod", "worker-{0}.prod",
]

INFO_TEMPLATES = [
    "User {user} logged in from {ip}",
    "User {user} logged out",
    "Request {req} processed in {ms}ms",
    "Cache hit for key={key}",
    "Database query completed: {rows} rows in {ms}ms",
    "Background job {job} started",
    "Background job {job} completed in {sec}s",
    "Configuration reloaded from {path}",
    "Connection established to {host}:{port}",
    "Health check passed for {service}",
    "Heartbeat sent to {service} (interval=30s)",
    "Order {order} placed by {user}",
    "Payment {payment} authorized for ${amount}",
    "Notification dispatched to {user}",
    "Webhook delivered to {host}:{port} (status=200)",
    "Search query '{q}' returned {rows} results",
    "Feature flag '{flag}' evaluated to {bool} for user={user}",
    "Rate limit reset for {ip}",
    "Session created for {user} (sid={key})",
    "API token issued for {user}",
]

WARN_TEMPLATES = [
    "Slow query detected: {ms}ms (threshold {threshold}ms)",
    "Memory usage at {pct}% on {host}",
    "Retry {retry} of 5 for operation {op}",
    "Cache miss for key={key} - falling back to database",
    "Connection pool '{pool}' at {pct}% capacity",
    "Throttled request from {ip} ({req} requests in last minute)",
    "Deprecated API endpoint /{api} called by {service}",
    "Disk usage at {pct}% on {host}",
    "Token nearing expiry for user={user}",
    "Webhook delivery slow: {ms}ms to {host}",
    "Queue depth elevated: {rows} pending in {service}",
]

ERROR_TEMPLATES = [
    "Authentication failed for user={user} - invalid credentials",
    "Database query timeout after {sec}s for operation {op}",
    "Connection refused to {host}:{port}",
    "Failed to parse JSON input: {detail}",
    "Operation {op} failed: {detail}",
    "HTTP {code} returned from {service}",
    "Webhook delivery failed to {host}:{port} (status={code})",
    "Payment {payment} declined: insufficient funds for {user}",
    "Order {order} canceled: inventory unavailable",
    "Notification failed for user={user}: {detail}",
    "Worker {service} disconnected from queue {pool}",
    "Search query '{q}' failed: {detail}",
]

CRITICAL_TEMPLATES = [
    "Unhandled exception System.OutOfMemoryException: Insufficient memory in {service}",
    "StackOverflowException occurred in thread {thread}",
    "Database connection pool '{pool}' exhausted",
    "Disk full on {drive}: only {free}MB remaining on {host}",
    "Service {service} unresponsive for {sec}s — failover triggered",
    "Runtime: access violation at 0x{addr:08X} reading address 0x{addr2:08X}",
    "Faulting application {service}.exe, faulting module ntdll.dll",
    "FATAL: data corruption detected in shard {rows}",
]

# Distribution of severity. Mostly Info, with realistic tail.
LEVEL_WEIGHTS = [
    ("INFO",     INFO_TEMPLATES,     70),
    ("WARNING",  WARN_TEMPLATES,     15),
    ("ERROR",    ERROR_TEMPLATES,    10),
    ("CRITICAL", CRITICAL_TEMPLATES,  5),
]


def random_ip():
    return ".".join(str(random.randint(1, 254)) for _ in range(4))


def random_key(n=12):
    chars = "abcdef0123456789"
    return "".join(random.choice(chars) for _ in range(n))


def fill(template):
    return template.format(
        user=random.choice(USERS),
        service=random.choice(SERVICES),
        host=random.choice(HOSTS).format(random.randint(1, 12)),
        ip=random_ip(),
        req=random.randint(1000, 999_999),
        ms=random.randint(1, 5000),
        threshold=random.choice([100, 250, 500, 1000]),
        rows=random.randint(0, 100_000),
        sec=random.randint(1, 300),
        job=random.choice(["nightly-rollup", "daily-billing", "cache-warm", "index-rebuild", "report-gen"]),
        path=random.choice(["/etc/app/config.yaml", "C:\\ProgramData\\App\\config.json", "/opt/app/settings.toml"]),
        port=random.choice([80, 443, 5432, 6379, 8080, 8443, 9092]),
        order=f"ORD-{random.randint(100000, 999999)}",
        payment=f"PAY-{random.randint(10000, 99999)}",
        amount=f"{random.uniform(1, 5000):.2f}",
        q=random.choice(["coffee mug", "winter jacket", "laptop charger", "blue widget", "hex bolt"]),
        flag=random.choice(["new-checkout", "dark-mode", "ab-test-v2", "rate-limit-v3"]),
        bool=random.choice(["true", "false"]),
        key=random_key(),
        retry=random.randint(1, 5),
        op=random.choice(["read", "write", "update", "delete", "scan", "sync"]),
        pool=random.choice(["primary", "replica", "analytics", "session"]),
        api=random.choice(["v1/users", "v1/orders", "v2/payments", "legacy/auth"]),
        pct=random.randint(70, 99),
        code=random.choice([400, 401, 403, 404, 500, 502, 503]),
        detail=random.choice([
            "timeout exceeded",
            "connection reset by peer",
            "permission denied",
            "resource not found",
            "invalid argument",
            "checksum mismatch",
        ]),
        thread=random.choice(["Main", "Worker-1", "Worker-2", "Pool-Thread-3", "RequestHandler"]),
        drive=random.choice(["C:\\", "D:\\", "/var/log"]),
        free=random.randint(1, 500),
        addr=random.randint(0, 0xFFFFFFFF),
        addr2=random.randint(0, 0xFFFFFFFF),
    )


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--lines", type=int, default=500_000)
    ap.add_argument("--out", default=os.path.join("Samples", "large-sample.log"))
    ap.add_argument("--start", default="2026-04-01T00:00:00",
                    help="ISO start time; entries are spaced ~150ms apart on average.")
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args()

    random.seed(args.seed)
    start = datetime.datetime.fromisoformat(args.start)

    # Pre-build the cumulative weight table for level selection.
    levels = []
    cum = 0
    for name, templates, weight in LEVEL_WEIGHTS:
        cum += weight
        levels.append((cum, name, templates))
    total_weight = cum

    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)
    bytes_written = 0
    t = start
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        for i in range(args.lines):
            # Advance time by 50–300ms; truncate to seconds for the timestamp.
            t += datetime.timedelta(milliseconds=random.randint(50, 300))
            stamp = t.strftime("%Y-%m-%d %H:%M:%S")

            r = random.uniform(0, total_weight)
            for ceiling, name, templates in levels:
                if r < ceiling:
                    msg = fill(random.choice(templates))
                    line = f"[{stamp}] {name}: {msg}\n"
                    f.write(line)
                    bytes_written += len(line)
                    break

    print(f"Wrote {args.lines:,} lines, {bytes_written:,} bytes ({bytes_written / (1024*1024):.1f} MiB) to {args.out}")
    print(f"Time range: {start.isoformat()} -> {t.isoformat()}")


if __name__ == "__main__":
    main()
