# Docker startup diagnosis

## Reproduced failure

Fresh project `lablend-t20-951a7532ccc3`, source `783c2f8`, separate empty volumes and generated credentials. Five independent .NET services exited 139: Catalog, Inventory, Borrowing API, Borrowing Worker and Notification. Every captured state had `OOMKilled=false`. Identity, Gateway, nginx, PostgreSQL and Redis remained running. See [captured states, exceptions and PostgreSQL timeline](evidence/final-startup-race.json).

The worker log contains an unhandled `NpgsqlException` with `SocketException (111): Connection refused` while opening the database in `BorrowingMigrations.Apply`. Catalog/Inventory/Notification report the same connection refusal. Borrowing API reports PostgreSQL SQLSTATE `57P03`, “the database system is starting up.” These are actual dependency-startup failures, not evidence of a loan-rule or gRPC-contract defect. Exit 139 alone did not establish the cause.

## Timeline and configuration defect

| UTC 2026-11-16 | Actual observation |
|---|---|
| 18:46:58 | PostgreSQL temporary bootstrap server starts and listens only on a Unix socket; reports ready for local init scripts |
| 18:47:03 | Compose starts the dependent .NET services after the old healthcheck reports healthy |
| 18:47:05–14 | Bootstrap server shuts down; local probes see shutting-down state |
| 18:47:21 | Final PostgreSQL process opens IPv4/IPv6 TCP port 5432 and becomes ready |
| 18:47:26–31 | Five dependants terminate after their initial migration connection fails |

The actual image entrypoint's `docker_temp_server_start()` explicitly sets `listen_addresses=''`. The previous health command, `pg_isready -U postgres -d postgres`, implicitly used the Unix socket and accepted this temporary server. The APIs use TCP DNS `postgres:5432`, so the old health gate did not represent their real dependency. Bootstrap shutting down between the probe and migration explains both connection refusal and 57P03.

The same worker image was also started with only its actual dependencies in the diagnostic project. It applied all owned migrations, completed all four scheduled jobs, authenticated Inventory readiness and became healthy. The image was linux/amd64. This ruled out a reproducible image-architecture mismatch for that image; it did not prove unlimited resource capacity.

## Correction and scope

Compose now probes **TCP** using `pg_isready -h 127.0.0.1 -U postgres -d postgres`. The init-only Unix-socket server cannot satisfy it. Dependent startup therefore waits for the final PostgreSQL process, after the database/role init scripts finish. No database owner, grant, schema migration or business contract changed.

`scripts/Start-Core.ps1` starts services one at a time and waits for real health, including Identity/Catalog/Inventory/Borrowing before the Worker. README and the T20 harness use it to reduce peak startup pressure on this small Docker VM. It performs no mutation retries, resets, pruning or volume deletion. This operational sequencing supplements the corrected TCP gate; it is not a fabricated explanation of the exit code.

At one failure inspection Windows had only about 13 MiB free out of 5.94 GiB visible RAM; Docker containers shared about 2.81 GiB. This is a measured resource constraint, **not proof of a kernel OOM kill**, since captured OOM flags were false. No matching kernel OOM/segfault lines were returned by the targeted log query. No memory/WSL/security settings were changed.

Fresh-clone verification of the fix and the full production-web scenario are recorded separately in [final acceptance](FINAL_PRODUCT_ACCEPTANCE.md). Failed attempts remain part of the record. The older M3 shared-stack 139 had no retained crash log and cannot be retroactively assigned a cause with certainty; the cold-install defect above is reproduced and explained by direct evidence.
