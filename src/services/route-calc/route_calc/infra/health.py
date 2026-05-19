import asyncio
import logging

logger = logging.getLogger(__name__)

_RESPONSE_OK = (
    b"HTTP/1.1 200 OK\r\n"
    b"Content-Type: application/json\r\n"
    b"Content-Length: 15\r\n"
    b"\r\n"
    b'{"status":"ok"}'
)
_RESPONSE_404 = b"HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n"

_HEALTH_PATHS = {b"/health", b"/health/live", b"/health/ready"}


async def _handle(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
    try:
        line = await asyncio.wait_for(reader.readline(), timeout=5)
        parts = line.split()
        path = parts[1] if len(parts) >= 2 else b""
        response = _RESPONSE_OK if path in _HEALTH_PATHS else _RESPONSE_404
        if path == b"/health":
            logger.info("Health check called")
        writer.write(response)
        await writer.drain()
    except Exception:
        pass
    finally:
        writer.close()


async def start_health_server(host: str = "0.0.0.0", port: int = 8080) -> asyncio.Server:
    server = await asyncio.start_server(_handle, host, port)
    logger.info("Health server listening on %s:%d", host, port)
    return server
