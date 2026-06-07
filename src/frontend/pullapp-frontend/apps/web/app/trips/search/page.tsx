'use client';

import dynamic from 'next/dynamic';
import React from "react";
import { Input, Button } from '@pullapp/ui';
import { useSearchTrips } from '@pullapp/features';

// Importujemy mapę z folderu publish, aby nie duplikować kodu komponentu
const MapWithNoSSR = dynamic(
    () => import('../publish/components/Map'),
    { ssr: false, loading: () => <p>Ładowanie interaktywnej mapy...</p> }
);

export default function SearchTripPage() {
    const { searchTrips, isLoading, matches, error } = useSearchTrips();

    // 1. Stan dla punktów geograficznych pobieranych z mapy
    const [coordinates, setCoordinates] = React.useState<{
        start: { lat: number; lng: number } | null;
        end: { lat: number; lng: number } | null;
    }>({ start: null, end: null });

    // 2. Stan dla dodatkowych parametrów formularza z refactor_plan.md
    const [formParams, setFormParams] = React.useState({
        departureDate: '',
        seatsNeeded: 1,
        maxDetourKm: 5,
        timeWindowMinutes: 30
    });

    const handleRouteSelected = (start: any, end: any) => {
        setCoordinates({ start, end });
    };

    const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const { name, value } = e.target;
        setFormParams(prev => ({ ...prev, [name]: value }));
    };

    const handleSearchSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        
        if (!coordinates.start || !coordinates.end) return;

        const payload = {
            start: coordinates.start,
            end: coordinates.end,
            departureDate: new Date(formParams.departureDate).toISOString(),
            seatsNeeded: Number(formParams.seatsNeeded),
            maxDetourKm: Number(formParams.maxDetourKm),
            timeWindowMinutes: Number(formParams.timeWindowMinutes)
        };

        await searchTrips(payload);
    };

    return (
        <div style={{ maxWidth: '800px', margin: '0 auto', padding: '2rem' }}>
            <h1>Znajdź dopasowanie przejazdu 🎒</h1>
            <p>Wybierz punkty na mapie i określ kryteria wyszukiwania.</p>

            <div style={{ margin: '2rem 0', borderRadius: '12px', overflow: 'hidden' }}>
                <MapWithNoSSR onRouteSelected={handleRouteSelected} />
            </div>

            <form onSubmit={handleSearchSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1.5rem' }}>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
                    <div>
                        <label>Data odjazdu</label>
                        <input 
                            type="datetime-local" 
                            name="departureDate" 
                            value={formParams.departureDate} 
                            onChange={handleInputChange} 
                            required 
                            style={{ padding: '0.75rem', borderRadius: '8px', border: '1px solid #ccc', fontSize: '1rem' }}
                        />
                    </div>
                    <div>
                        <label>Potrzebne miejsca</label>
                        <input 
                            type="number" 
                            name="seatsNeeded" 
                            min={1} 
                            max={8} 
                            value={formParams.seatsNeeded} 
                            onChange={handleInputChange} 
                            required 
                            style={{ padding: '0.75rem', borderRadius: '8px', border: '1px solid #ccc', fontSize: '1rem' }}
                        />
                    </div>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
                    <div>
                        <label>Maksymalny objazd (km)</label>
                        <input 
                            type="number" 
                            name="maxDetourKm" 
                            min={1} 
                            value={formParams.maxDetourKm} 
                            onChange={handleInputChange} 
                            required 
                            style={{ padding: '0.75rem', borderRadius: '8px', border: '1px solid #ccc', fontSize: '1rem' }}
                        />
                    </div>
                    <div>
                        <label>Okno czasowe (minuty)</label>
                        <input 
                            type="number" 
                            name="timeWindowMinutes" 
                            min={5} 
                            value={formParams.timeWindowMinutes} 
                            onChange={handleInputChange} 
                            required 
                            style={{ padding: '0.75rem', borderRadius: '8px', border: '1px solid #ccc', fontSize: '1rem' }}
                        />
                    </div>
                </div>

                <button 
                    type="submit" 
                    disabled={isLoading || !coordinates.start || !coordinates.end}
                    style={{ padding: '1rem', backgroundColor: '#3498db', color: 'white', border: 'none', borderRadius: '8px', fontSize: '1.1rem', cursor: 'pointer', opacity: (!coordinates.start || !coordinates.end) ? 0.5 : 1 }}
                >
                    {isLoading ? 'Szukanie...' : 'Szukaj przejazdów'}
                </button>
            </form>
        </div>
    );
}