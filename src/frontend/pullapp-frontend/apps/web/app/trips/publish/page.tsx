'use client';

import dynamic from 'next/dynamic';

// Wyłączamy SSR dla mapy, aby uniknąć błędu "window is not defined"
const MapWithNoSSR = dynamic(
	() => import('./components/Map'),
	{ ssr: false, loading: () => <p>Ładowanie interaktywnej mapy...</p> }
);

export default function PublishTripPage() {
	return (
		<>
			<h1>Opublikuj nowy przejazd</h1>
			{/* Tutaj Twój formularz MediatR (Skąd, Dokąd, Data, Cena) */}
			
			<div style={{ margin: '2rem 0', borderRadius: '12px', overflow: 'hidden' }}>
				<MapWithNoSSR />
			</div>
		</>
	);
}