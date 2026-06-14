'use client';

import Link from 'next/link';
import { useRidesStore, useAuthStore, type RideStatus } from '@pullapp/features';

const STATUS: Record<RideStatus, { label: string; bg: string; color: string }> = {
	accepted:  { label: 'Zaakceptowany', bg: '#dcfce7', color: '#15803d' },
	started:   { label: 'W trakcie',     bg: '#dbeafe', color: '#1d4ed8' },
	ended:     { label: 'Zakończony',    bg: '#f3f4f6', color: '#6b7280' },
	cancelled: { label: 'Anulowany',     bg: '#fef2f2', color: '#b91c1c' },
};

export default function MyRidesPage() {
	const token = useAuthStore((s) => s.token);
	const rides = useRidesStore((s) => s.rides);
	const list = Object.values(rides).sort((a, b) => b.updatedAt - a.updatedAt);

	if (!token) {
		return (
			<div style={{ maxWidth: 700, margin: '4rem auto', padding: '2rem', textAlign: 'center', color: '#6b7280' }}>
				Zaloguj się, aby zobaczyć swoje przejazdy.
			</div>
		);
	}

	return (
		<div style={{ maxWidth: 700, margin: '0 auto', padding: '2rem 1.5rem' }} data-testid="my-rides">
			<h1 style={{ fontSize: '1.6rem', fontWeight: 600, marginBottom: '0.25rem' }}>Moje przejazdy</h1>
			<p style={{ color: '#6b7280', marginBottom: '1.5rem' }}>
				Przejazdy pojawiają się tu po zaakceptowaniu prośby przez kierowcę i aktualizują na żywo.
			</p>

			{list.length === 0 ? (
				<div style={{ padding: '3rem', textAlign: 'center', border: '1px dashed #e5e7eb', borderRadius: 12, color: '#9ca3af' }}>
					<div style={{ fontSize: '2rem', marginBottom: '0.75rem' }}>🧳</div>
					<div>Brak przejazdów.</div>
					<div style={{ fontSize: '0.82rem', marginTop: '0.5rem' }}>
						<Link href="/trips/search" style={{ color: '#2563eb' }}>Znajdź przejazd →</Link>
					</div>
				</div>
			) : (
				<div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
					{list.map((r) => {
						const s = STATUS[r.status];
						return (
							<div key={r.rideId} data-testid="ride-card" style={{ padding: '1.25rem', border: '1px solid #e5e7eb', borderRadius: 12, backgroundColor: '#fff' }}>
								<div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.75rem' }}>
									<div style={{ fontWeight: 500, color: '#111827' }}>Przejazd</div>
									<span data-testid={`ride-status-${r.status}`} style={{ padding: '3px 10px', borderRadius: 20, fontSize: '0.78rem', fontWeight: 500, backgroundColor: s.bg, color: s.color }}>
										{s.label}
									</span>
								</div>
								<div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, fontSize: '0.8rem', color: '#6b7280' }}>
									<div><span style={{ color: '#9ca3af' }}>Kierowca:</span> <code>{r.driverId?.slice(0, 8) ?? '—'}</code></div>
									<div><span style={{ color: '#9ca3af' }}>Trasa:</span> <code>{r.routeId?.slice(0, 8) ?? '—'}</code></div>
								</div>
							</div>
						);
					})}
				</div>
			)}
		</div>
	);
}
