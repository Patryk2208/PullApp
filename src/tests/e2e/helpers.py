import asyncio
import httpx
from httpx_sse import aconnect_sse, ServerSentEvent


async def sse_open(
    client: httpx.AsyncClient,
    url: str,
    headers: dict | None = None,
) -> tuple[asyncio.Queue, asyncio.Task]:
    """
    Opens an SSE connection and streams events into a queue.
    Waits until the HTTP connection is established before returning.
    Returns (queue, background_task) — caller must cancel the task when done.
    """
    queue: asyncio.Queue[ServerSentEvent] = asyncio.Queue()
    connected = asyncio.Event()
    connection_error: Exception | None = None

    async def _stream() -> None:
        nonlocal connection_error
        try:
            async with aconnect_sse(client, "GET", url, headers=headers or {}) as event_source:
                connected.set()
                async for sse in event_source.aiter_sse():
                    await queue.put(sse)
        except Exception as exc:
            connection_error = exc
        finally:
            connected.set()

    task = asyncio.create_task(_stream())
    await connected.wait()

    if connection_error is not None:
        task.cancel()
        raise connection_error

    return queue, task


async def sse_wait(
    queue: asyncio.Queue,
    *event_types: str,
    timeout: float = 30,
) -> tuple[str | None, str | None]:
    """
    Reads from queue until one of event_types arrives.
    Returns (event_type, data) or (None, None) on timeout.
    """
    loop = asyncio.get_running_loop()
    deadline = loop.time() + timeout
    while True:
        remaining = deadline - loop.time()
        if remaining <= 0:
            return None, None
        try:
            sse: ServerSentEvent = await asyncio.wait_for(queue.get(), timeout=remaining)
            if sse.event in event_types:
                return sse.event, sse.data
        except asyncio.TimeoutError:
            return None, None


async def register_route(
    client: httpx.AsyncClient,
    driver_id: str,
    start: tuple[float, float] = (52.2297, 21.0122),
    end: tuple[float, float] = (52.2500, 21.0300),
) -> httpx.Response:
    return await client.post(
        "/api/driver/route",
        json={"start": {"lat": start[0], "lng": start[1]}, "end": {"lat": end[0], "lng": end[1]}},
        headers={"X-User-Id": driver_id},
    )


async def create_passenger_request(
    client: httpx.AsyncClient,
    passenger_id: str,
    start: tuple[float, float] = (52.2310, 21.0140),
    end: tuple[float, float] = (52.2480, 21.0280),
) -> httpx.Response:
    return await client.post(
        "/api/passenger/route-requests",
        json={"start": {"lat": start[0], "lng": start[1]}, "end": {"lat": end[0], "lng": end[1]}},
        headers={"X-User-Id": passenger_id},
    )


def parse_id(response: httpx.Response, *keys: str) -> str:
    """Extracts a GUID field from a JSON response, trying each key in order (camelCase first)."""
    data = response.json()
    for key in keys:
        if key in data:
            return data[key]
    raise KeyError(f"None of {keys!r} found in response: {data}")
