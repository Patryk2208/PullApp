'use client';

import { useMapEvents } from 'react-leaflet';
import L from 'leaflet';

interface MapClickHandlerProps {
	startPoint: L.LatLng | null;
	endPoint: L.LatLng | null;
	onPointsChange: (start: L.LatLng | null, end: L.LatLng | null) => void;
}

export function MapClickHandler({ startPoint, endPoint, onPointsChange }: MapClickHandlerProps) {
	useMapEvents({
		click(e) {
			const clickedCoords = e.latlng;
			
			// Jeśli nie ma startu, ustawiamy start.
			// Jeśli jest start, ale nie ma mety, ustawiamy metę.
			// Jeśli są oba punkty, resetujemy i ustawiamy nowy start
			
			if (!startPoint) {
				onPointsChange(clickedCoords, null);
			} else if (!endPoint) {
				onPointsChange(startPoint, clickedCoords);
			} else {
				onPointsChange(clickedCoords, null);
			}
		},
	});
	
	return null; // Ten komponent nie renderuje HTML, tylko zarządza zdarzeniami.
}