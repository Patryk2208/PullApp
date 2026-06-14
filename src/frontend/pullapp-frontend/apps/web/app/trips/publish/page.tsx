'use client';

import dynamic from 'next/dynamic';
import React from "react";
import { usePublishTrip, useAuthStore, useNotificationStream, type SseEvent } from '@pullapp/features';

const MapWithNoSSR = dynamic(
    () => import('./components/Map'),
    { ssr: false, loading: () => <p>Ładowanie interaktywnej mapy...</p> }
);

export default function PublishTripPage() {
    const { publishTrip, isLoading, error, result } = usePublishTrip();

    const [coordinates, setCoordinates] = React.useState<{
        start: { lat: number; lng: number } | null;
        end: { lat: number; lng: number } | null;
    }>({ start: null, end: null });

    const [capacity, setCapacity] = React.useState(3);
	const [activateStatus, setActivateStatus] = React.useState<'idle' | 'loading' | 'success' | 'error'>('idle');
	const [activateError, setActivateError] = React.useState<string | null>(null);
	const [readyRouteId, setReadyRouteId] = React.useState<string | null>(null);
	const [deleted, setDeleted] = React.useState(false);
	const [deleteError, setDeleteError] = React.useState<string | null>(null);
	const token = useAuthStore(state => state.token);

	// flow 1.5 — usunięcie trasy (DELETE → 204; 403 route_not_deletable jeśli aktywna z rides)
	const handleDelete = async () => {
		if (!result) return;
		setDeleteError(null);
		try {
			const res = await fetch(`/api/route/driver/routes/${result.routeId}`, {
				method: 'DELETE',
				headers: token ? { 'Authorization': `Bearer ${token}` } : undefined,
			});
			if (!res.ok) {
				let msg = `Błąd: ${res.status}`;
				try { const j = await res.json(); msg = j?.Message || msg; } catch {}
				throw new Error(msg);
			}
			setDeleted(true);
		} catch (err: any) {
			setDeleteError(err.message);
		}
	};

	// geometria liczy się async po publish; backend emituje `route_ready` gdy gotowa.
	// Zapisujemy id (event może dojść przed albo po odpowiedzi publish) i dopiero wtedy
	// odblokowujemy aktywację — eliminuje race 409 „activate za wcześnie".
	const handleStream = React.useCallback((e: SseEvent) => {
		if (e.type === 'route_ready' && e.data?.RouteId) setReadyRouteId(e.data.RouteId);
	}, []);
	useNotificationStream(handleStream);
	const routeReady = !!result && readyRouteId === result.routeId;

	const handleActivate = async () => {
		if (!result || !coordinates.start) return;
		setActivateStatus('loading');
		setActivateError(null);
		try {
			const response = await fetch(`/api/route/driver/routes/${result.routeId}/activate`, {
				method: 'POST',
				headers: {
					'Content-Type': 'application/json',
					...(token ? { 'Authorization': `Bearer ${token}` } : {})
				},
				body: JSON.stringify({
					CurrentLocation: {
						Latitude: coordinates.start.lat,
						Longitude: coordinates.start.lng
					}
				})
			});
			if (!response.ok) {
				const text = await response.text();
				try { throw new Error(JSON.parse(text).Message || `Błąd: ${response.status}`); }
				catch { throw new Error(`Błąd: ${response.status}`); }
			}
			setActivateStatus('success');
		} catch (err: any) {
			setActivateStatus('error');
			setActivateError(err.message);
		}
	};

    const handleRouteSelected = (start: any, end: any) => {
        setCoordinates({ start, end });
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        if (!coordinates.start || !coordinates.end) return;
        await publishTrip({
            start: coordinates.start,
            end: coordinates.end,
            capacity
        });
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
                Opublikuj przejazd
            </h1>
            <p style={{ color: '#6b7280', marginBottom: '1.5rem' }}>
                Zaznacz trasę na mapie i podaj liczbę wolnych miejsc.
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

            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                <div>
                    <label style={labelStyle}>Liczba wolnych miejsc</label>
                    <input
                        type="number"
                        min={1}
                        max={8}
                        value={capacity}
                        onChange={e => setCapacity(Number(e.target.value))}
                        required
                        style={{ ...inputStyle, maxWidth: '200px' }}
                    />
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
                    {isLoading ? 'Publikowanie...' : 'Opublikuj trasę'}
                </button>
            </form>

            {error && (
                <div style={{ marginTop: '1.5rem', padding: '1rem 1.25rem', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', color: '#b91c1c', fontSize: '0.9rem' }}>
                    {error}
                </div>
            )}

            {result && deleted && (
				<div data-testid="route-deleted" style={{ marginTop: '1.5rem', padding: '1.25rem', backgroundColor: '#f9fafb', border: '1px solid #e5e7eb', borderRadius: '12px', textAlign: 'center', color: '#6b7280' }}>
					🗑️ Trasa została usunięta.
				</div>
			)}
			{result && !deleted && (
				<div style={{ marginTop: '1.5rem', padding: '1.25rem', backgroundColor: '#f0fdf4', border: '1px solid #bbf7d0', borderRadius: '12px' }}>
					<div style={{ fontWeight: 600, color: '#15803d', marginBottom: '0.5rem', fontSize: '1rem' }}>
						Trasa opublikowana!
					</div>
					<div style={{ fontSize: '0.85rem', color: '#6b7280', marginBottom: '4px' }}>ID trasy</div>
					<div style={{ fontSize: '0.85rem', fontFamily: 'monospace', color: '#374151', wordBreak: 'break-all', marginBottom: '1rem' }}>
						{result.routeId}
					</div>
					{activateStatus !== 'success' ? (
						<>
							<div style={{ fontSize: '0.85rem', color: '#6b7280', backgroundColor: '#ecfdf5', padding: '0.75rem', borderRadius: '8px', marginBottom: '0.75rem' }}>
								{routeReady
									? 'Geometria trasy gotowa — możesz aktywować, aby zacząć przyjmować pasażerów.'
									: '⏳ Trwa obliczanie geometrii trasy… przycisk aktywacji odblokuje się automatycznie.'}
							</div>
							{activateError && (
								<div style={{ marginBottom: '0.75rem', padding: '0.75rem', backgroundColor: '#fef2f2', border: '1px solid #fecaca', borderRadius: '8px', color: '#b91c1c', fontSize: '0.85rem' }}>
									{activateError}
								</div>
							)}
							<button
								onClick={handleActivate}
								data-testid="activate-button"
								disabled={!routeReady || activateStatus === 'loading'}
								style={{
									width: '100%',
									padding: '0.85rem',
									backgroundColor: (!routeReady || activateStatus === 'loading') ? '#93c5fd' : '#16a34a',
									color: 'white',
									border: 'none',
									borderRadius: '8px',
									fontSize: '1rem',
									fontWeight: 500,
									cursor: (!routeReady || activateStatus === 'loading') ? 'not-allowed' : 'pointer',
								}}>
								{activateStatus === 'loading'
									? 'Aktywowanie...'
									: !routeReady ? '⏳ Czekam na gotowość trasy…' : '🟢 Aktywuj trasę — zaczynam jazdę'}
							</button>
							{deleteError && (
								<div style={{ marginTop: '0.5rem', color: '#b91c1c', fontSize: '0.82rem' }}>{deleteError}</div>
							)}
							<button
								data-testid="delete-route-button"
								onClick={handleDelete}
								style={{ width: '100%', marginTop: '0.5rem', padding: '0.6rem', backgroundColor: 'transparent', color: '#b91c1c', border: '1px solid #fecaca', borderRadius: '8px', fontSize: '0.85rem', fontWeight: 500, cursor: 'pointer' }}>
								🗑️ Usuń trasę
							</button>
						</>
					) : (
						<div style={{ padding: '0.75rem', backgroundColor: '#dcfce7', border: '1px solid #bbf7d0', borderRadius: '8px', color: '#15803d', fontSize: '0.9rem', textAlign: 'center', fontWeight: 500 }}>
							Trasa aktywna! Pasażerowie mogą Cię teraz znaleźć.
						</div>
					)}
				</div>
			)}
        </div>
    );
}