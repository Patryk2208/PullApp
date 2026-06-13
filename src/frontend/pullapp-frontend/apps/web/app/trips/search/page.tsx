'use client';

import dynamic from 'next/dynamic';
import React from "react";
import { useSearchTrips } from '@pullapp/features';
import { TripMatch } from '@pullapp/domain';

const MapWithNoSSR = dynamic(
    () => import('../publish/components/Map'),
    { ssr: false, loading: () => <p>Ładowanie interaktywnej mapy...</p> }
);

const ModalMapWithNoSSR = dynamic(
    () => import('./components/ModalMap'),
    { ssr: false, loading: () => <p>Ładowanie mapy...</p> }
);

export default function SearchTripPage() {
    const { searchTrips, isLoading, matches, error } = useSearchTrips();

    const [coordinates, setCoordinates] = React.useState<{
        start: { lat: number; lng: number } | null;
        end: { lat: number; lng: number } | null;
    }>({ start: null, end: null });

    const [formParams, setFormParams] = React.useState({
        departureDate: '',
        seatsNeeded: 1,
        maxDetourKm: 5,
        timeWindowMinutes: 30
    });

    const [selectedMatch, setSelectedMatch] = React.useState<TripMatch | null>(null);

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
        await searchTrips({
            start: coordinates.start,
            end: coordinates.end,
            departureDate: new Date(formParams.departureDate).toISOString(),
            seatsNeeded: Number(formParams.seatsNeeded),
            maxDetourKm: Number(formParams.maxDetourKm),
            timeWindowMinutes: Number(formParams.timeWindowMinutes)
        });
    };

    const scoreColor = (score: number) => {
        if (score >= 0.85) return '#16a34a';
        if (score >= 0.65) return '#ca8a04';
        return '#dc2626';
    };

    const inputStyle: React.CSSProperties = {
        width: '100%',
        padding: '0.65rem 0.75rem',
        borderRadius: '8px',
        border: '1px solid #d1d5db',
        fontSize: '0.95rem',
        boxSizing: 'border-box',
        marginTop: '4px'
    };

    const labelStyle: React.CSSProperties = {
        fontSize: '0.85rem',
        fontWeight: 500,
        color: '#374151'
    };

    return (
        <div style={{ maxWidth: '800px', margin: '0 auto', padding: '2rem 1.5rem' }}>
            <h1 style={{ fontSize: '1.6rem', fontWeight: 600, marginBottom: '0.25rem' }}>
                Znajdź przejazd
            </h1>
            <p style={{ color: '#6b7280', marginBottom: '1.5rem' }}>
                Zaznacz punkt startowy i docelowy na mapie, uzupełnij szczegóły i wyszukaj pasujące trasy.
            </p>

            <div style={{ borderRadius: '12px', overflow: 'hidden', border: '1px solid #e5e7eb', marginBottom: '1.5rem' }}>
                <MapWithNoSSR onRouteSelected={handleRouteSelected} />
            </div>

            {coordinates.start && coordinates.end && (
                <div style={{ display: 'flex', gap: '8px', marginBottom: '1rem' }}>
                    <span style={{ background: '#dcfce7', color: '#15803d', padding: '3px 10px', borderRadius: '20px', fontSize: '0.82rem' }}>
                        Start wybrany
                    </span>
                    <span style={{ background: '#fee2e2', color: '#b91c1c', padding: '3px 10px', borderRadius: '20px', fontSize: '0.82rem' }}>
                        Cel wybrany
                    </span>
                </div>
            )}

            <form onSubmit={handleSearchSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
                    <div>
                        <label style={labelStyle}>Data odjazdu</label>
                        <input type="datetime-local" name="departureDate" value={formParams.departureDate}
                            onChange={handleInputChange} required style={inputStyle} />
                    </div>
                    <div>
                        <label style={labelStyle}>Potrzebne miejsca</label>
                        <input type="number" name="seatsNeeded" min={1} max={8}
                            value={formParams.seatsNeeded} onChange={handleInputChange} required style={inputStyle} />
                    </div>
                </div>

                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1rem' }}>
                    <div>
                        <label style={labelStyle}>Maks. objazd (km)</label>
                        <input type="number" name="maxDetourKm" min={1}
                            value={formParams.maxDetourKm} onChange={handleInputChange} required style={inputStyle} />
                    </div>
                    <div>
                        <label style={labelStyle}>Okno czasowe (min)</label>
                        <input type="number" name="timeWindowMinutes" min={5}
                            value={formParams.timeWindowMinutes} onChange={handleInputChange} required style={inputStyle} />
                    </div>
                </div>

                <button
                    type="submit"
                    disabled={isLoading || !coordinates.start || !coordinates.end}
                    style={{
                        padding: '0.85rem',
                        backgroundColor: (!coordinates.start || !coordinates.end || isLoading) ? '#93c5fd' : '#2563eb',
                        color: 'white',
                        border: 'none',
                        borderRadius: '8px',
                        fontSize: '1rem',
                        fontWeight: 500,
                        cursor: (!coordinates.start || !coordinates.end || isLoading) ? 'not-allowed' : 'pointer',
                    }}>
                    {isLoading ? '🔍 Szukanie dopasowań...' : 'Szukaj przejazdów'}
                </button>
            </form>

            {error && (
                <div style={{ marginTop: '1.5rem', padding: '1rem 1.25rem', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', color: '#b91c1c', fontSize: '0.9rem' }}>
                    {error}
                </div>
            )}

            {matches.length > 0 && (
                <div style={{ marginTop: '2rem' }}>
                    <h2 style={{ fontSize: '1.1rem', fontWeight: 600, marginBottom: '1rem', color: '#111827' }}>
                        Znalezione przejazdy ({matches.length})
                    </h2>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                        {matches.map((match) => (
                            <div
                                key={match.routeId}
                                onClick={() => setSelectedMatch(match)}
                                style={{
                                    padding: '1rem 1.25rem',
                                    border: '1px solid #e5e7eb',
                                    borderRadius: '12px',
                                    backgroundColor: '#ffffff',
                                    cursor: 'pointer',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'space-between',
                                    gap: '1rem'
                                }}
                                onMouseEnter={e => {
                                    (e.currentTarget as HTMLDivElement).style.borderColor = '#93c5fd';
                                    (e.currentTarget as HTMLDivElement).style.boxShadow = '0 2px 8px rgba(37,99,235,0.08)';
                                }}
                                onMouseLeave={e => {
                                    (e.currentTarget as HTMLDivElement).style.borderColor = '#e5e7eb';
                                    (e.currentTarget as HTMLDivElement).style.boxShadow = 'none';
                                }}
                            >
                                <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                                    <div style={{
                                        width: '42px', height: '42px', borderRadius: '50%',
                                        backgroundColor: '#eff6ff',
                                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                                        fontSize: '1.2rem', flexShrink: 0
                                    }}>
                                        🚗
                                    </div>
                                    <div>
                                        <div style={{ fontWeight: 500, fontSize: '0.9rem', color: '#111827', marginBottom: '2px' }}>
                                            Kierowca
                                        </div>
                                        <div style={{ fontSize: '0.78rem', color: '#6b7280', fontFamily: 'monospace' }}>
                                            {match.driverId.slice(0, 8)}...
                                        </div>
                                    </div>
                                </div>

                                <div style={{ display: 'flex', gap: '1.5rem', alignItems: 'center' }}>
                                    <div style={{ textAlign: 'center' }}>
                                        <div style={{ fontSize: '1.1rem', fontWeight: 600, color: scoreColor(match.matchScore) }}>
                                            {(match.matchScore * 100).toFixed(0)}%
                                        </div>
                                        <div style={{ fontSize: '0.72rem', color: '#9ca3af' }}>dopasowanie</div>
                                    </div>
                                    <div style={{ textAlign: 'center' }}>
                                        <div style={{ fontSize: '1.1rem', fontWeight: 600, color: '#111827' }}>
                                            {match.detourKm.toFixed(1)} km
                                        </div>
                                        <div style={{ fontSize: '0.72rem', color: '#9ca3af' }}>objazd</div>
                                    </div>
                                    <div style={{
                                        padding: '6px 14px',
                                        backgroundColor: '#eff6ff',
                                        color: '#2563eb',
                                        borderRadius: '20px',
                                        fontSize: '0.8rem',
                                        fontWeight: 500,
                                        whiteSpace: 'nowrap'
                                    }}>
                                        Zobacz na mapie →
                                    </div>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {selectedMatch && coordinates.start && coordinates.end && (
                <div
                    onClick={(e) => { if (e.target === e.currentTarget) setSelectedMatch(null); }}
                    style={{
                        position: 'fixed', inset: 0,
                        backgroundColor: 'rgba(0,0,0,0.5)',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        zIndex: 1000, padding: '1rem'
                    }}>
                    <div style={{
                        backgroundColor: '#ffffff',
                        borderRadius: '16px',
                        width: '100%',
                        maxWidth: '600px',
                        maxHeight: '90vh',
                        overflow: 'auto',
                        boxShadow: '0 20px 60px rgba(0,0,0,0.3)'
                    }}>
                        <div style={{ padding: '1.25rem 1.5rem', borderBottom: '1px solid #e5e7eb', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <h3 style={{ margin: 0, fontWeight: 600, fontSize: '1rem' }}>Szczegóły przejazdu</h3>
                            <button
                                onClick={() => setSelectedMatch(null)}
                                style={{ background: 'none', border: 'none', fontSize: '1.4rem', cursor: 'pointer', color: '#6b7280', lineHeight: 1 }}>
                                ×
                            </button>
                        </div>

                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '1px', backgroundColor: '#e5e7eb', borderBottom: '1px solid #e5e7eb' }}>
                            {[
                                { label: 'Dopasowanie', value: `${(selectedMatch.matchScore * 100).toFixed(0)}%`, color: scoreColor(selectedMatch.matchScore) },
                                { label: 'Objazd', value: `${selectedMatch.detourKm.toFixed(1)} km`, color: '#111827' },
                                { label: 'Indeks odbioru', value: `#${selectedMatch.pickupPointIndex}`, color: '#111827' },
                            ].map(({ label, value, color }) => (
                                <div key={label} style={{ backgroundColor: '#f9fafb', padding: '1rem', textAlign: 'center' }}>
                                    <div style={{ fontSize: '1.25rem', fontWeight: 600, color }}>{value}</div>
                                    <div style={{ fontSize: '0.75rem', color: '#9ca3af', marginTop: '2px' }}>{label}</div>
                                </div>
                            ))}
                        </div>

                        <div style={{ height: '300px', borderBottom: '1px solid #e5e7eb' }}>
                            <ModalMapWithNoSSR
                                start={coordinates.start}
                                end={coordinates.end}
                            />
                        </div>

                        <div style={{ padding: '1rem 1.5rem' }}>
                            <div style={{ fontSize: '0.8rem', color: '#9ca3af', marginBottom: '4px' }}>ID trasy</div>
                            <div style={{ fontSize: '0.85rem', fontFamily: 'monospace', color: '#374151', wordBreak: 'break-all' }}>
                                {selectedMatch.routeId}
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}