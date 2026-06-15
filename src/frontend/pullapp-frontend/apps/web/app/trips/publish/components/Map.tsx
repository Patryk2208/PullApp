'use client';

import { MapContainer, TileLayer, Marker, Popup } from 'react-leaflet';
import 'leaflet/dist/leaflet.css';
import L from 'leaflet';
import * as React from 'react';
import { MapClickHandler } from './MapClickHandler';
import 'leaflet/dist/leaflet.css';

// Fix na domyślne ikony Leafleta w Next.js (Web-pack gubi ścieżki do markerów)
// @ts-ignore
delete L.Icon.Default.prototype._getIconUrl;
L.Icon.Default.mergeOptions({
	iconUrl: 'https://unpkg.com/leaflet@1.7.1/dist/images/marker-icon.png',
	shadowUrl: 'https://unpkg.com/leaflet@1.7.1/dist/images/marker-shadow.png',
});

// Przygotowanie ładnych, standardowych ikon z zewnętrznego CDN (różne kolory)
const startIcon = new L.Icon({
	iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-green.png',
	shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/0.7.7/images/marker-shadow.png',
	iconSize: [25, 41],
	iconAnchor: [12, 41],
	popupAnchor: [1, -34],
	shadowSize: [41, 41]
});

const endIcon = new L.Icon({
	iconUrl: 'https://raw.githubusercontent.com/pointhi/leaflet-color-markers/master/img/marker-icon-2x-red.png',
	shadowUrl: 'https://cdnjs.cloudflare.com/ajax/libs/leaflet/0.7.7/images/marker-shadow.png',
	iconSize: [25, 41],
	iconAnchor: [12, 41],
	popupAnchor: [1, -34],
	shadowSize: [41, 41]
});

interface MapProps {
	onRouteSelected: (
		start: { lat: number; lng: number } | null,
		end: { lat: number; lng: number } | null
	) => void;
}

export default function Map({ onRouteSelected }: MapProps) {
	const [startPoint, setStartPoint] = React.useState<L.LatLng | null>(null);
	const [endPoint, setEndPoint] = React.useState<L.LatLng | null>(null);
	
	const handlePointsChange = (start: L.LatLng | null, end: L.LatLng | null) => {
		setStartPoint(start);
		setEndPoint(end);
		
		// Przekazujemy czyste współrzędne w górę do formularza Next.js
		onRouteSelected(
			start ? { lat: start.lat, lng: start.lng } : null,
			end ? { lat: end.lat, lng: end.lng } : null
		);
	};
	
	const handleReset = () => {
		handlePointsChange(null, null);
	};
	
	return (
		<div style={{ width: '100%' }}>
			<div style={{ marginBottom: '1rem', display: 'flex', gap: '1rem', alignItems: 'center' }}>
				<div>
					<strong>Status: </strong>
					{!startPoint && 'Skąd zaczynasz? Kliknij na mapie, aby wybrać miejsce startu.'}
					{startPoint && !endPoint && 'Teraz kliknij, aby wybrać punkt docelowy'}
					{startPoint && endPoint && 'Trasa wybrana! Możesz zapisać formularz.'}
				</div>
				{(startPoint || endPoint) && (
					<button onClick={handleReset} style={{ padding: '0.3rem 0.6rem', cursor: 'pointer' }}>
						Wyczyść punkty
					</button>
				)}
			</div>
			
			<MapContainer
				center={[52.2297, 21.0122]}
				zoom={12}
				style={{ height: '450px', width: '100%', borderRadius: '8px' }}
			>
				<TileLayer
					attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
					url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
				/>
				
				{/* Komponent nasłuchujący kliknięć */}
				<MapClickHandler
					startPoint={startPoint}
					endPoint={endPoint}
					onPointsChange={handlePointsChange}
				/>
				
				{/* Renderowanie markera startu */}
				{startPoint && (
					<Marker position={startPoint} icon={startIcon}>
						<Popup>A: Miejsce rozpoczęcia podróży</Popup>
					</Marker>
				)}
				
				{/* Renderowanie markera mety */}
				{endPoint && (
					<Marker position={endPoint} icon={endIcon}>
						<Popup>B: Miejsce docelowe</Popup>
					</Marker>
				)}
			</MapContainer>
		</div>
	);
}