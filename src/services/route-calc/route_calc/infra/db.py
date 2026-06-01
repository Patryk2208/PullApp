import logging
from typing import List

from psycopg_pool import ConnectionPool

from route_calc.model.algorithms import DriverRoute
from route_calc.model.common import Point

_log = logging.getLogger(__name__)

_ACTIVE_ROUTES_SQL = """
    SELECT id,
           driver_id,
           capacity - active_ride_count AS seats_free,
           ST_AsText(route_geom)        AS geom_wkt
    FROM   routes
    WHERE  status = 'Active'
      AND  capacity - active_ride_count > 0
      AND  route_geom IS NOT NULL
"""


def _parse_wkt(wkt: str) -> List[Point]:
    # "LINESTRING(lon lat, lon lat, ...)"
    inner = wkt[11:-1]
    points = []
    for pair in inner.split(","):
        lon_s, lat_s = pair.strip().split()
        points.append(Point(lat=float(lat_s), lon=float(lon_s)))
    return points


class RouteDB:
    def __init__(self, config: dict, logger: logging.Logger = None):
        self._logger = logger or _log
        cfg = config
        conninfo = (
            f"host={cfg['host']} port={cfg['port']} "
            f"dbname={cfg['database']} user={cfg['username']} password={cfg['password']}"
        )
        pool_cfg = cfg.get("pool", {})
        self._pool = ConnectionPool(
            conninfo=conninfo,
            min_size=pool_cfg.get("min_connections", 2),
            max_size=pool_cfg.get("max_connections", 20),
            open=True,
        )

    def get_active_routes(self) -> List[DriverRoute]:
        with self._pool.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(_ACTIVE_ROUTES_SQL)
                rows = cur.fetchall()

        routes = []
        for route_id, driver_id, seats_free, geom_wkt in rows:
            try:
                points = _parse_wkt(geom_wkt)
            except Exception:
                self._logger.warning("Failed to parse WKT for route %s — skipping", route_id)
                continue
            routes.append(DriverRoute(
                route_id=str(route_id),
                driver_id=str(driver_id),
                route_points=points,
                seats_available=seats_free,
            ))
        return routes

    def close(self):
        self._pool.close()
