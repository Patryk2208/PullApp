'use client';

import dynamic from 'next/dynamic';
import React from "react";

// Wyłączamy SSR dla mapy, aby uniknąć błędu "window is not defined"
const MapWithNoSSR = dynamic(
	() => import('./components/Map'),
	{ ssr: false, loading: () => <p>Ładowanie interaktywnej mapy...</p> }
);

export default function PublishTripPage() {
	const [tripCoordinates, setTripCoordinates] = React.useState<{
		start: { lat: number; lng: number } | null;
		end: { lat: number; lng: number } | null;
	}>({ start: null, end: null });
	
	const handleRouteSelected = (start: any, end: any) => {
		setTripCoordinates({ start, end });
	};
	
	return (
		<>
			<h1>Opublikuj nowy przejazd</h1>
			{/* Tutaj formularz MediatR (Skąd, Dokąd, Data, Cena) */}
			
			<div style={{ margin: '2rem 0', borderRadius: '12px', overflow: 'hidden' }}>
				<MapWithNoSSR onRouteSelected={handleRouteSelected} />
			</div>
			
			{/* Sekcja pomocnicza: pokazuje, że strona już "widzi" dane z mapy */}
			<div style={{ background: '#f5f5f5', padding: '1rem', borderRadius: '8px' }}>
				<h3>Wybrane punkty dla backendu:</h3>
				<p>📍 <strong>Start:</strong> {tripCoordinates.start ? `${tripCoordinates.start.lat.toFixed(4)}, ${tripCoordinates.start.lng.toFixed(4)}` : 'Nie wybrano'}</p>
				<p>🏁 <strong>Meta:</strong> {tripCoordinates.end ? `${tripCoordinates.end.lat.toFixed(4)}, ${tripCoordinates.end.lng.toFixed(4)}` : 'Nie wybrano'}</p>
			</div>
		</>
	);
}