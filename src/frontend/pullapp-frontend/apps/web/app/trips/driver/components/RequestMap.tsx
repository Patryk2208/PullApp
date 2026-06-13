'use client';

import { MapContainer, TileLayer, Marker, Popup } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';

// @ts-ignore
delete L.Icon.Default.prototype._getIconUrl;

const greenIcon = new L.Icon({
    iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-green.png',
    shadowUrl: 'https://unpkg.com/leaflet@1.7.1/dist/images/marker-shadow.png',
    iconSize: [25, 41], iconAnchor: [12, 41],
});

const redIcon = new L.Icon({
    iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-red.png',
    shadowUrl: 'https://unpkg.com/leaflet@1.7.1/dist/images/marker-shadow.png',
    iconSize: [25, 41], iconAnchor: [12, 41],
});

interface Props {
    start: { Lat: number; Lng: number };
    end: { Lat: number; Lng: number };
}

export default function RequestMap({ start, end }: Props) {
    const midLat = (start.Lat + end.Lat) / 2;
    const midLng = (start.Lng + end.Lng) / 2;

    return (
        <MapContainer
            center={[midLat, midLng]}
            zoom={14}
            style={{ width: '100%', height: '100%' }}
            scrollWheelZoom={false}
        >
            <TileLayer
                url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
                attribution="© OpenStreetMap"
            />
            <Marker position={[start.Lat, start.Lng]} icon={greenIcon}>
                <Popup>Odbiór pasażera</Popup>
            </Marker>
            <Marker position={[end.Lat, end.Lng]} icon={redIcon}>
                <Popup>Wysiadanie pasażera</Popup>
            </Marker>
        </MapContainer>
    );
}