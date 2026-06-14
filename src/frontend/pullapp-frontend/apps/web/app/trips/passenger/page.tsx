'use client';

import React, { useEffect, useState } from 'react';
import { useAuthStore } from '@pullapp/features';

interface Ride {
    rideId: string;
    routeId: string;
    driverId: string;
    status: 'accepted' | 'picking_up' | 'picked_up' | 'started' | 'error';
    driverDeclared?: boolean;
    passengerDeclared?: boolean;
}

export default function PassengerDashboardPage() {
    const token = useAuthStore(state => state.token);
    const [rides, setRides] = useState<Ride[]>([]);
    const [sseConnected, setSseConnected] = useState(false);

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
                                console.log(`SSE Event: ${eventType}`, data);

                                if (eventType === 'ride_accepted') {
                                    setRides(prev => [...prev, {
                                        rideId: data.RideId,
                                        routeId: data.RouteId,
                                        driverId: data.DriverId,
                                        status: 'accepted'
                                    }]);
                                } else if (eventType === 'driver_declared_pickup') {
                                    setRides(prev => prev.map(r =>
                                        r.rideId.toLowerCase() === data.RideId.toLowerCase()
                                            ? { ...r, driverDeclared: true }
                                            : r
                                    ));
                                } else if (eventType === 'ride_started') {
                                    setRides(prev => prev.map(r =>
                                        r.rideId.toLowerCase() === data.RideId.toLowerCase()
                                            ? { ...r, status: 'started' }
                                            : r
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

    const handlePickup = async (rideId: string) => {
        setRides(prev => prev.map(r =>
            r.rideId === rideId ? { ...r, status: 'picking_up' } : r
        ));
        try {
            const response = await fetch(`/api/route/passenger/rides/${rideId}/pickup`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                }
            });
            if (!response.ok) throw new Error(`Błąd: ${response.status}`);
            
            setRides(prev => prev.map(r =>
                r.rideId === rideId 
                    ? { ...r, status: 'picked_up', passengerDeclared: true } 
                    : r
            ));
        } catch (err: any) {
            setRides(prev => prev.map(r =>
                r.rideId === rideId ? { ...r, status: 'error' } : r
            ));
        }
    };

    if (!token) {
        return (
            <div style={{ maxWidth: '700px', margin: '4rem auto', padding: '2rem', textAlign: 'center' }}>
                <p style={{ color: '#6b7280' }}>Zaloguj się aby zobaczyć panel pasażera.</p>
            </div>
        );
    }

    return (
        <div style={{ maxWidth: '700px', margin: '0 auto', padding: '2rem 1.5rem' }}>
            <h1 style={{ fontSize: '1.6rem', fontWeight: 600, marginBottom: '0.25rem' }}>
                Panel pasażera
            </h1>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '2rem' }}>
                <div style={{
                    width: '8px', height: '8px', borderRadius: '50%',
                    backgroundColor: sseConnected ? '#16a34a' : '#d1d5db'
                }} />
                <span style={{ fontSize: '0.82rem', color: '#6b7280' }}>
                    {sseConnected ? 'Nasłuchuję na statusy przejazdów...' : 'Łączenie...'}
                </span>
            </div>

            {rides.length === 0 ? (
                <div style={{
                    padding: '3rem',
                    textAlign: 'center',
                    border: '1px dashed #e5e7eb',
                    borderRadius: '12px',
                    color: '#9ca3af'
                }}>
                    <div style={{ fontSize: '2rem', marginBottom: '0.75rem' }}>🎒</div>
                    <div>Brak aktywnych przejazdów.</div>
                    <div style={{ fontSize: '0.82rem', marginTop: '0.5rem' }}>
                        Gdy kierowca zaakceptuje Twoją prośbę, pojawi się tutaj.
                    </div>
                </div>
            ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                    {rides.map(ride => (
                        <div key={ride.rideId} style={{
                            padding: '1.25rem',
                            border: '1px solid #e5e7eb',
                            borderRadius: '12px',
                            backgroundColor: ride.status === 'started' ? '#f0fdf4' : '#ffffff',
                        }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: '1rem' }}>
                                <div>
                                    <div style={{ fontWeight: 500, fontSize: '0.9rem', color: '#111827', marginBottom: '4px' }}>
                                        Twoja podróż
                                    </div>
                                    <div style={{ fontSize: '0.78rem', color: '#6b7280', fontFamily: 'monospace' }}>
                                        Kierowca: {ride.driverId.slice(0, 8)}...
                                    </div>
                                </div>
                                <div style={{
                                    padding: '3px 10px',
                                    borderRadius: '20px',
                                    fontSize: '0.78rem',
                                    fontWeight: 500,
                                    backgroundColor: (ride.status === 'started' || ride.status === 'accepted') ? '#dcfce7' : '#fef9c3',
                                    color: (ride.status === 'started' || ride.status === 'accepted') ? '#15803d' : '#854d0e',
                                }}>
                                    {ride.status === 'accepted' ? 'Zaakceptowano' :
                                     ride.status === 'picking_up' ? 'Wysyłanie...' :
                                     ride.status === 'picked_up' ? 'Oczekiwanie na start' :
                                     ride.status === 'started' ? 'W drodze' : 'Błąd'}
                                </div>
                            </div>

                            <div style={{ display: 'flex', gap: '8px', marginBottom: '1.5rem' }}>
                                {ride.driverDeclared ? (
                                    <span style={{ background: '#dcfce7', color: '#15803d', padding: '3px 10px', borderRadius: '20px', fontSize: '0.75rem', fontWeight: 500 }}>
                                        Kierowca potwierdził odbiór ✓
                                    </span>
                                ) : (
                                    <span style={{ background: '#fef2f2', color: '#b91c1c', padding: '3px 10px', borderRadius: '20px', fontSize: '0.75rem', fontWeight: 500 }}>
                                        Kierowca jeszcze Cię nie widzi
                                    </span>
                                )}
                                {ride.passengerDeclared && (
                                    <span style={{ background: '#dcfce7', color: '#15803d', padding: '3px 10px', borderRadius: '20px', fontSize: '0.75rem', fontWeight: 500 }}>
                                        Ty: potwierdziłeś wejście ✓
                                    </span>
                                )}
                            </div>

                            {(ride.status === 'accepted' || ride.status === 'picking_up') && (
                                <button
                                    onClick={() => handlePickup(ride.rideId)}
                                    disabled={!ride.driverDeclared || ride.status === 'picking_up'}
                                    style={{
                                        width: '100%',
                                        padding: '0.75rem',
                                        backgroundColor: ride.driverDeclared ? '#2563eb' : '#d1d5db',
                                        color: 'white',
                                        border: 'none',
                                        borderRadius: '8px',
                                        fontSize: '0.95rem',
                                        fontWeight: 600,
                                        cursor: ride.driverDeclared ? 'pointer' : 'not-allowed',
                                    }}>
                                    {!ride.driverDeclared ? 'Czekaj na potwierdzenie kierowcy' : 
                                     ride.status === 'picking_up' ? 'Wysyłanie...' : 'Już jestem w aucie!'}
                                </button>
                            )}

                            {ride.status === 'started' && (
                                <div style={{ textAlign: 'center', padding: '1rem', color: '#15803d', fontWeight: 500 }}>
                                    ✨ Jesteś w drodze! Miłej podróży.
                                </div>
                            )}
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
}
