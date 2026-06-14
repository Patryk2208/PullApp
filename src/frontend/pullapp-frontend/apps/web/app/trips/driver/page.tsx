'use client';

import React, { useEffect, useState } from 'react';
import { useAuthStore } from '@pullapp/features';
import dynamic from 'next/dynamic';

const RequestMapWithNoSSR = dynamic(
    () => import('./components/RequestMap'),
    { ssr: false, loading: () => <p>Ładowanie mapy...</p> }
);

interface RideRequest {
    requestId: string;
    routeId: string;
    rideId?: string;
    passengerId: string;
    startPoint: { Lat: number; Lng: number };
    endPoint: { Lat: number; Lng: number };
    driverDeclared?: boolean;
    passengerDeclared?: boolean;
}

type RequestStatus = 'pending' | 'accepting' | 'rejecting' | 'accepted' | 'rejected' | 'picking_up' | 'picked_up' | 'started' | 'error';

interface RequestCard {
    request: RideRequest;
    status: RequestStatus;
    error?: string;
}

export default function DriverDashboardPage() {
    const token = useAuthStore(state => state.token);
    const [cards, setCards] = useState<RequestCard[]>([]);
    const [sseConnected, setSseConnected] = useState(false);
    const [mapCard, setMapCard] = useState<RequestCard | null>(null);

    useEffect(() => {
        if (!token) return;

        const controller = new AbortController();

        const connectSSE = async () => {
            try {
                const response = await fetch('/api/sse', {
                    headers: {
                        'Authorization': `Bearer ${token}`,
                        'Accept': 'text/event-stream',
                    },
                    signal: controller.signal,
                });

                if (!response.ok || !response.body) {
                    console.error("SSE connection failed:", response.status);
                    return;
                }

                setSseConnected(true);
                const reader = response.body.getReader();
                const decoder = new TextDecoder();
                let buffer = '';

                while (true) {
                    const { done, value } = await reader.read();
                    if (done) break;

                    const chunk = decoder.decode(value, { stream: true });
                    console.log("Driver SSE chunk:", chunk);
                    buffer += chunk;
                    const lines = buffer.split('\n');
                    buffer = lines.pop() ?? '';

                    let eventType = '';
                    for (const line of lines) {
                        if (line.startsWith('event:')) {
                            eventType = line.replace('event:', '').trim();
                        } else if (line.startsWith('data:')) {
                            try {
                                const data = JSON.parse(line.replace('data:', '').trim());
                                if (eventType === 'ride_requested') {
                                    console.log("Driver otrzymał ride_requested:", data);
                                    setCards(prev => [...prev, {
                                        request: {
                                            requestId: data.RequestId,
                                            routeId: data.RouteId,
                                            passengerId: data.PassengerId,
                                            startPoint: { Lat: data.StartPoint.Latitude, Lng: data.StartPoint.Longitude },
                                            endPoint: { Lat: data.EndPoint.Latitude, Lng: data.EndPoint.Longitude },
                                        },
                                        status: 'pending'
                                    }]);
                                } else if (eventType === 'passenger_declared_pickup') {
                                    console.log("Driver otrzymał passenger_declared_pickup:", data);
                                    setCards(prev => prev.map(c =>
                                        c.request.rideId && c.request.rideId.toLowerCase() === data.RideId.toLowerCase()
                                            ? { ...c, request: { ...c.request, passengerDeclared: true } }
                                            : c
                                    ));
                                } else if (eventType === 'ride_started') {
                                    console.log("Driver otrzymał ride_started:", data);
                                    setCards(prev => prev.map(c =>
                                        c.request.rideId && c.request.rideId.toLowerCase() === data.RideId.toLowerCase()
                                            ? { ...c, status: 'started' as RequestStatus }
                                            : c
                                    ));
                                }
                            } catch (e) {
                                console.error("Error parsing SSE data:", e);
                            }
                            eventType = '';
                        }
                    }
                }
            } catch (err: any) {
                if (err.name !== 'AbortError') {
                    setSseConnected(false);
                }
            }
        };

        connectSSE();
        return () => {
            controller.abort();
            setSseConnected(false);
        };
    }, [token]);

    const handleAccept = async (requestId: string) => {
        setCards(prev => prev.map(c =>
            c.request.requestId === requestId ? { ...c, status: 'accepting' } : c
        ));
        try {
            const response = await fetch(`/api/route/driver/requests/${requestId}/accept`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                }
            });
            if (!response.ok) throw new Error(`Błąd: ${response.status}`);
            
            const result = await response.json();
            setCards(prev => prev.map(c =>
                c.request.requestId === requestId 
                    ? { ...c, status: 'accepted', request: { ...c.request, rideId: result.rideId } } 
                    : c
            ));
        } catch (err: any) {
            setCards(prev => prev.map(c =>
                c.request.requestId === requestId ? { ...c, status: 'error', error: err.message } : c
            ));
        }
    };

    const handlePickup = async (rideId: string) => {
        setCards(prev => prev.map(c =>
            c.request.rideId === rideId ? { ...c, status: 'picking_up' } : c
        ));
        try {
            const response = await fetch(`/api/route/driver/rides/${rideId}/pickup`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                }
            });
            if (!response.ok) throw new Error(`Błąd: ${response.status}`);
            
            setCards(prev => prev.map(c =>
                c.request.rideId === rideId 
                    ? { ...c, status: 'picked_up', request: { ...c.request, driverDeclared: true } } 
                    : c
            ));
        } catch (err: any) {
            setCards(prev => prev.map(c =>
                c.request.rideId === rideId ? { ...c, status: 'error', error: err.message } : c
            ));
        }
    };

    const handleReject = async (requestId: string) => {
        setCards(prev => prev.map(c =>
            c.request.requestId === requestId ? { ...c, status: 'rejecting' } : c
        ));
        try {
            const response = await fetch(`/api/route/driver/requests/${requestId}/reject`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                }
            });
            if (!response.ok) throw new Error(`Błąd: ${response.status}`);
            setCards(prev => prev.map(c =>
                c.request.requestId === requestId ? { ...c, status: 'rejected' } : c
            ));
        } catch (err: any) {
            setCards(prev => prev.map(c =>
                c.request.requestId === requestId ? { ...c, status: 'error', error: err.message } : c
            ));
        }
    };

    if (!token) {
        return (
            <div style={{ maxWidth: '700px', margin: '4rem auto', padding: '2rem', textAlign: 'center' }}>
                <p style={{ color: '#6b7280' }}>Zaloguj się aby zobaczyć panel kierowcy.</p>
            </div>
        );
    }

    return (
        <div style={{ maxWidth: '700px', margin: '0 auto', padding: '2rem 1.5rem' }}>
            <h1 style={{ fontSize: '1.6rem', fontWeight: 600, marginBottom: '0.25rem' }}>
                Panel kierowcy
            </h1>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '2rem' }}>
                <div style={{
                    width: '8px', height: '8px', borderRadius: '50%',
                    backgroundColor: sseConnected ? '#16a34a' : '#d1d5db'
                }} />
                <span style={{ fontSize: '0.82rem', color: '#6b7280' }}>
                    {sseConnected ? 'Nasłuchuję na nowe prośby...' : 'Łączenie...'}
                </span>
            </div>

            {cards.length === 0 ? (
                <div style={{
                    padding: '3rem',
                    textAlign: 'center',
                    border: '1px dashed #e5e7eb',
                    borderRadius: '12px',
                    color: '#9ca3af'
                }}>
                    <div style={{ fontSize: '2rem', marginBottom: '0.75rem' }}>🚗</div>
                    <div>Brak nowych próśb o dołączenie.</div>
                    <div style={{ fontSize: '0.82rem', marginTop: '0.5rem' }}>
                        Gdy pasażer wyśle prośbę, pojawi się tutaj automatycznie.
                    </div>
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                    {cards.map(card => (
                        <div key={card.request.requestId} style={{
                            padding: '1.25rem',
                            border: `1px solid ${card.status === 'accepted' ? '#bbf7d0' : card.status === 'rejected' ? '#e5e7eb' : '#e5e7eb'}`,
                            borderRadius: '12px',
                            backgroundColor: card.status === 'accepted' ? '#f0fdf4' : card.status === 'rejected' ? '#f9fafb' : '#ffffff',
                            opacity: card.status === 'rejected' ? 0.6 : 1,
                        }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '1rem' }}>
                                <div>
                                    <div style={{ fontWeight: 500, fontSize: '0.9rem', color: '#111827', marginBottom: '4px' }}>
                                        Prośba o dołączenie
                                    </div>
                                    <div style={{ fontSize: '0.78rem', color: '#6b7280', fontFamily: 'monospace' }}>
                                        Pasażer: {card.request.passengerId.slice(0, 8)}...
                                    </div>
                                </div>
                                <div style={{
                                    padding: '3px 10px',
                                    borderRadius: '20px',
                                    fontSize: '0.78rem',
                                    fontWeight: 500,
                                    backgroundColor: (card.status === 'accepted' || card.status === 'picked_up' || card.status === 'started') ? '#dcfce7' :
                                        card.status === 'rejected' ? '#f3f4f6' :
                                        card.status === 'error' ? '#fef2f2' : '#fef9c3',
                                    color: (card.status === 'accepted' || card.status === 'picked_up' || card.status === 'started') ? '#15803d' :
                                        card.status === 'rejected' ? '#6b7280' :
                                        card.status === 'error' ? '#b91c1c' : '#854d0e',
                                }}>
                                    {card.status === 'pending' ? 'Oczekuje' :
                                     card.status === 'accepting' ? 'Akceptowanie...' :
                                     card.status === 'rejecting' ? 'Odrzucanie...' :
                                     card.status === 'accepted' ? 'Zaakceptowano' :
                                     card.status === 'picking_up' ? 'Wysyłanie...' :
                                     card.status === 'picked_up' ? 'Odebrano' :
                                     card.status === 'started' ? 'W drodze' :
                                     card.status === 'rejected' ? 'Odrzucono' : 'Błąd'}
                                </div>
                            </div>

                            <div style={{ display: 'flex', gap: '8px', marginBottom: '1rem' }}>
                                {card.request.driverDeclared && (
                                    <span style={{ background: '#dcfce7', color: '#15803d', padding: '3px 10px', borderRadius: '20px', fontSize: '0.75rem', fontWeight: 500 }}>
                                        Kierowca potwierdził ✓
                                    </span>
                                )}
                                {card.request.passengerDeclared && (
                                    <span style={{ background: '#dcfce7', color: '#15803d', padding: '3px 10px', borderRadius: '20px', fontSize: '0.75rem', fontWeight: 500 }}>
                                        Pasażer potwierdził ✓
                                    </span>
                                )}
                            </div>

                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '8px', marginBottom: '1rem', fontSize: '0.82rem' }}>
                                <div style={{ padding: '8px 10px', backgroundColor: '#f9fafb', borderRadius: '8px' }}>
                                    <div style={{ color: '#9ca3af', marginBottom: '2px' }}>Odbiór</div>
                                    <div style={{ color: '#374151', fontFamily: 'monospace' }}>
                                        {card.request.startPoint.Lat.toFixed(4)}, {card.request.startPoint.Lng.toFixed(4)}
                                    </div>
                                </div>
                                <div style={{ padding: '8px 10px', backgroundColor: '#f9fafb', borderRadius: '8px' }}>
                                    <div style={{ color: '#9ca3af', marginBottom: '2px' }}>Wysiadanie</div>
                                    <div style={{ color: '#374151', fontFamily: 'monospace' }}>
                                        {card.request.endPoint.Lat.toFixed(4)}, {card.request.endPoint.Lng.toFixed(4)}
                                    </div>
                                </div>
                            </div>

                            {card.status === 'error' && (
                                <div style={{ marginBottom: '0.75rem', padding: '0.5rem 0.75rem', backgroundColor: '#fef2f2', borderRadius: '6px', color: '#b91c1c', fontSize: '0.82rem' }}>
                                    {card.error}
                                </div>
                            )}

                            <button
                                onClick={() => setMapCard(card)}
                                style={{
                                    marginBottom: '8px',
                                    width: '100%',
                                    padding: '0.6rem',
                                    backgroundColor: 'white',
                                    color: '#2563eb',
                                    border: '1px solid #bfdbfe',
                                    borderRadius: '8px',
                                    fontSize: '0.85rem',
                                    fontWeight: 500,
                                    cursor: 'pointer',
                                }}>
                                Zobacz trasę pasażera na mapie →
                            </button>

                            {(card.status === 'pending' || card.status === 'error') && (
                                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '8px' }}>
                                    <button
                                        onClick={() => handleReject(card.request.requestId)}
                                        style={{
                                            padding: '0.65rem',
                                            backgroundColor: 'white',
                                            color: '#374151',
                                            border: '1px solid #d1d5db',
                                            borderRadius: '8px',
                                            fontSize: '0.9rem',
                                            fontWeight: 500,
                                            cursor: 'pointer',
                                        }}>
                                        Odrzuć
                                    </button>
                                    <button
                                        onClick={() => handleAccept(card.request.requestId)}
                                        style={{
                                            padding: '0.65rem',
                                            backgroundColor: '#2563eb',
                                            color: 'white',
                                            border: 'none',
                                            borderRadius: '8px',
                                            fontSize: '0.9rem',
                                            fontWeight: 500,
                                            cursor: 'pointer',
                                        }}>
                                        Akceptuj
                                    </button>
                                </div>
                            )}

                            {(card.status === 'accepted' || card.status === 'picking_up') && card.request.rideId && (
                                <button
                                    onClick={() => handlePickup(card.request.rideId!)}
                                    disabled={card.status === 'picking_up'}
                                    style={{
                                        width: '100%',
                                        padding: '0.65rem',
                                        backgroundColor: '#16a34a',
                                        color: 'white',
                                        border: 'none',
                                        borderRadius: '8px',
                                        fontSize: '0.9rem',
                                        fontWeight: 600,
                                        cursor: card.status === 'picking_up' ? 'not-allowed' : 'pointer',
                                    }}>
                                    {card.status === 'picking_up' ? 'Wysyłanie...' : 'Odbierz pasażera'}
                                </button>
                            )}
                        </div>
                    ))}
                </div>
            )}
        {mapCard && (
            <div
                onClick={(e) => { if (e.target === e.currentTarget) setMapCard(null); }}
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
                    overflow: 'hidden',
                    boxShadow: '0 20px 60px rgba(0,0,0,0.3)'
                }}>
                    <div style={{ padding: '1.25rem 1.5rem', borderBottom: '1px solid #e5e7eb', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                        <div>
                            <h3 style={{ margin: 0, fontWeight: 600, fontSize: '1rem' }}>Trasa pasażera</h3>
                            <div style={{ fontSize: '0.78rem', color: '#6b7280', marginTop: '2px', fontFamily: 'monospace' }}>
                                {mapCard.request.passengerId.slice(0, 8)}...
                            </div>
                        </div>
                        <button
                            onClick={() => setMapCard(null)}
                            style={{ background: 'none', border: 'none', fontSize: '1.4rem', cursor: 'pointer', color: '#6b7280' }}>
                            ×
                        </button>
                    </div>
                    <div style={{ height: '350px' }}>
                        <RequestMapWithNoSSR
                            start={mapCard.request.startPoint}
                            end={mapCard.request.endPoint}
                        />
                    </div>
                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1px', backgroundColor: '#e5e7eb' }}>
                        <div style={{ backgroundColor: '#f9fafb', padding: '0.75rem 1rem' }}>
                            <div style={{ fontSize: '0.75rem', color: '#9ca3af', marginBottom: '2px' }}>Odbiór</div>
                            <div style={{ fontSize: '0.82rem', fontFamily: 'monospace', color: '#374151' }}>
                                {mapCard.request.startPoint.Lat.toFixed(4)}, {mapCard.request.startPoint.Lng.toFixed(4)}
                            </div>
                        </div>
                        <div style={{ backgroundColor: '#f9fafb', padding: '0.75rem 1rem' }}>
                            <div style={{ fontSize: '0.75rem', color: '#9ca3af', marginBottom: '2px' }}>Wysiadanie</div>
                            <div style={{ fontSize: '0.82rem', fontFamily: 'monospace', color: '#374151' }}>
                                {mapCard.request.endPoint.Lat.toFixed(4)}, {mapCard.request.endPoint.Lng.toFixed(4)}
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        )}
        </div>
    );
}
