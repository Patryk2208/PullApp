import { useState, useEffect, useRef } from 'react';
import { MapContainer, TileLayer, Marker, Polyline } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import './App.css';

delete (L.Icon.Default.prototype as any)._getIconUrl;
L.Icon.Default.mergeOptions({
  iconRetinaUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-icon-2x.png',
  iconUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-icon.png',
  shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.7.1/images/marker-shadow.png',
});

interface Location {
  lat: number;
  lng: number;
  displayName: string;
}

function MapFixer() {
  const map = useRef<any>(null);
  useEffect(() => {
    setTimeout(() => {
      if (map.current?.leafletElement) {
        map.current.leafletElement.invalidateSize();
      }
    }, 250);
  }, []);
  return null;
}

export default function App() {
  const [startAddress, setStartAddress] = useState('Warszawa');
  const [destAddress, setDestAddress] = useState('Kraków');

  const [viaPoints, setViaPoints] = useState<string[]>(['']);

  const [start, setStart] = useState<Location | null>(null);
  const [destination, setDestination] = useState<Location | null>(null);
  const [viaLocations, setViaLocations] = useState<Location[]>([]);
  const [routeCoords, setRouteCoords] = useState<[number, number][]>([]);

  const geocode = async (address: string): Promise<Location | null> => {
    if (!address.trim()) return null;

    const url = `https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(address)}&limit=1&countrycodes=pl`;

    try {
      const res = await fetch(url);
      const data = await res.json();
      if (data && data.length > 0) {
        return {
          lat: parseFloat(data[0].lat),
          lng: parseFloat(data[0].lon),
          displayName: data[0].display_name,
        };
      }
    } catch (err) {
      console.error('Geocoding error:', err);
    }
    return null;
  };

  const getRoute = async () => {
    const startLoc = await geocode(startAddress);
    const destLoc = await geocode(destAddress);
    const viaLocs: Location[] = [];

    for (const point of viaPoints) {
      if (point.trim()) {
        const loc = await geocode(point);
        if (loc) viaLocs.push(loc);
      }
    }

    if (!startLoc || !destLoc) {
      alert('Start and destination are required.');
      return;
    }

    setStart(startLoc);
    setDestination(destLoc);
    setViaLocations(viaLocs);

    let coordsStr = `${startLoc.lng},${startLoc.lat}`;
    viaLocs.forEach(v => coordsStr += `;${v.lng},${v.lat}`);
    coordsStr += `;${destLoc.lng},${destLoc.lat}`;

    const routeUrl = `https://router.project-osrm.org/route/v1/driving/${coordsStr}?overview=full&geometries=geojson`;

    try {
      const res = await fetch(routeUrl);
      const data = await res.json();

      if (data.routes && data.routes.length > 0) {
        const coords = data.routes[0].geometry.coordinates.map(
          ([lng, lat]: [number, number]) => [lat, lng] as [number, number]
        );
        setRouteCoords(coords);
      } else {
        alert('No route found.');
      }
    } catch (err) {
      alert('Failed to fetch route.');
      console.error(err);
    }
  };

  const sendToBackend = () => {
    if (!start || !destination) {
      alert('Please search for a route first');
      return;
    }

    const payload = {
      start: { address: startAddress, location: start },
      destination: { address: destAddress, location: destination },
      viaPoints: viaPoints.filter(p => p.trim()),
      viaLocations,
      route: routeCoords,
      timestamp: new Date().toISOString(),
    };

    console.log('Sending to backend:', payload);
  };

  const addViaPoint = () => setViaPoints([...viaPoints, '']);
  const removeViaPoint = (index: number) => {
    const newPoints = viaPoints.filter((_, i) => i !== index);
    setViaPoints(newPoints.length === 0 ? [''] : newPoints);
  };
  const updateViaPoint = (index: number, value: string) => {
    const newPoints = [...viaPoints];
    newPoints[index] = value;
    setViaPoints(newPoints);
  };

  return (
    <div className="app-container">
      
      {/* Controls Panel */}
      <div className="controls-panel">
        <h2>Pull App Route Planner</h2>

        {/* Start */}
        <div className="form-group">
          <label className="label">From:</label>
          <input
            className="input-field"
            type="text"
            value={startAddress}
            onChange={(e) => setStartAddress(e.target.value)}
            placeholder="Warszawa"
          />
        </div>

        {/* Via Points */}
        <div className="form-group via-points">
          <label className="label via-points-label">Via Points (optional stops):</label>
          {viaPoints.map((point, index) => (
            <div key={index} className="via-point-item">
              <input
                className="input-field"
                type="text"
                value={point}
                onChange={(e) => updateViaPoint(index, e.target.value)}
                placeholder={`Stop ${index + 1}`}
              />
              <button 
                onClick={() => removeViaPoint(index)}
                className="remove-button"
              >
                ✕
              </button>
            </div>
          ))}
          <button 
            onClick={addViaPoint}
            className="button-add"
          >
            + Add another stop
          </button> 
        </div>

        {/* Destination */}
        <div className="form-group destination">
          <label className="label">To:</label>
          <input
            className="input-field"
            type="text"
            value={destAddress}
            onChange={(e) => setDestAddress(e.target.value)}
            placeholder="Kraków"
          />
        </div>

        <div className="button-group">
          <button
            onClick={getRoute}
            className="button-primary"
          >
            Find Route
          </button>
          <button
            onClick={sendToBackend}
            className="button-success"
          >
            Send to Backend
          </button>
        </div>
      </div>

      {/* Map */}
      <MapContainer
        center={[52.2297, 21.0122]}
        zoom={8}
        className="map-container"
        zoomControl={false}
      >
        <TileLayer
          attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
          url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
        />

        {start && <Marker position={[start.lat, start.lng]} />}
        {viaLocations.map((loc, i) => (
          <Marker key={i} position={[loc.lat, loc.lng]} />
        ))}
        {destination && <Marker position={[destination.lat, destination.lng]} />}

        {routeCoords.length > 0 && (
          <Polyline
            positions={routeCoords}
            color="#007AFF"
            weight={6}
            opacity={0.8}
          />
        )}

        <MapFixer />
      </MapContainer>
    </div>
  );
}