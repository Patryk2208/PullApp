import asyncio
import logging
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from route_calc.infra.queue import ComputeQueue

logger = logging.getLogger(__name__)

_BODY_HEALTHY   = b'{"status":"healthy"}'
_BODY_UNHEALTHY = b'{"status":"unhealthy","checks":{"rabbitmq":{"status":"unhealthy"}}}'


def _http_response(status: int, body: bytes) -> bytes:
    reason = b"OK" if status == 200 else b"Service Unavailable"
    return (
        b"HTTP/1.1 " + str(status).encode() + b" " + reason + b"\r\n"
        b"Content-Type: application/json\r\n"
        + b"Content-Length: " + str(len(body)).encode() + b"\r\n"
        + b"\r\n" + body
    )


_LIVE_RESPONSE    = _http_response(200, _BODY_HEALTHY)
_NOT_FOUND        = b"HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n"


def _is_rabbit_healthy(queue: "ComputeQueue | None") -> bool:
    if queue is None:
        return False
    conn = queue.connection
    return conn is not None and not conn.is_closed


async def _handle(
    reader: asyncio.StreamReader,
    writer: asyncio.StreamWriter,
    queue: "ComputeQueue | None",
) -> None:
    try:
        line = await asyncio.wait_for(reader.readline(), timeout=5)
        parts = line.split()
        path = parts[1] if len(parts) >= 2 else b""

        if path == b"/health/live":
            writer.write(_LIVE_RESPONSE)
        elif path in (b"/health", b"/health/ready"):
            if _is_rabbit_healthy(queue):
                writer.write(_http_response(200, _BODY_HEALTHY))
            else:
                writer.write(_http_response(503, _BODY_UNHEALTHY))
        else:
            writer.write(_NOT_FOUND)

        await writer.drain()
    except Exception:
        pass
    finally:
        writer.close()


async def start_health_server(
    queue: "ComputeQueue | None" = None,
    host: str = "0.0.0.0",
    port: int = 8080,
) -> asyncio.Server:
    async def handler(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        await _handle(reader, writer, queue)

    server = await asyncio.start_server(handler, host, port)
    logger.info("Health server listening on %s:%d", host, port)
    return server
