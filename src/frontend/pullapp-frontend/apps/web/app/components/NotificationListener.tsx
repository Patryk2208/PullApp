'use client';

import { useCallback, useState } from 'react';
import { useNotificationStream, useRidesStore, type SseEvent } from '@pullapp/features';

type ToastKind = 'success' | 'error' | 'info';
interface Toast { id: number; kind: ToastKind; text: string; }

let idSeq = 1;

const KIND_STYLE: Record<ToastKind, { bg: string; border: string; color: string }> = {
	success: { bg: '#f0fdf4', border: '#bbf7d0', color: '#15803d' },
	error:   { bg: '#fef2f2', border: '#fecaca', color: '#b91c1c' },
	info:    { bg: '#eff6ff', border: '#bfdbfe', color: '#1d4ed8' },
};

/**
 * Globalny nasłuch powiadomień (montowany w layout). Pokazuje toasty dla
 * zdarzeń istotnych dla pasażera — domyka pętlę zwrotną request→accept/reject,
 * której wcześniej brakowało (useSearchTrips abortował SSE po dopasowaniu).
 */
export function NotificationListener() {
	const [toasts, setToasts] = useState<Toast[]>([]);

	const push = useCallback((kind: ToastKind, text: string) => {
		const id = idSeq++;
		setToasts((prev) => [...prev, { id, kind, text }]);
		setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== id)), 6000);
	}, []);

	const applyRideEvent = useRidesStore((s) => s.applyEvent);

	const handleEvent = useCallback((e: SseEvent) => {
		// zasil store „Moich przejazdów" (jedyne źródło — backend nie ma GET)
		applyRideEvent(e.type, e.data);
		switch (e.type) {
			case 'ride_accepted':
				push('success', 'Kierowca zaakceptował Twoją prośbę! 🎉');
				break;
			case 'ride_rejected':
				push('error', 'Kierowca odrzucił Twoją prośbę.');
				break;
			case 'ride_ended':
				push('info', 'Przejazd został zakończony.');
				break;
			case 'route_deleted':
				push('info', 'Trasa, na którą czekałeś, została usunięta.');
				break;
		}
	}, [push, applyRideEvent]);

	useNotificationStream(handleEvent);

	if (toasts.length === 0) return null;

	return (
		<div
			data-testid="toast-container"
			style={{ position: 'fixed', top: 16, right: 16, zIndex: 2000, display: 'flex', flexDirection: 'column', gap: 8, maxWidth: 360 }}
		>
			{toasts.map((t) => {
				const s = KIND_STYLE[t.kind];
				return (
					<div
						key={t.id}
						role="status"
						data-testid={`toast-${t.kind}`}
						style={{
							padding: '0.75rem 1rem',
							backgroundColor: s.bg,
							border: `1px solid ${s.border}`,
							color: s.color,
							borderRadius: 10,
							fontSize: '0.9rem',
							boxShadow: '0 6px 20px rgba(0,0,0,0.12)',
						}}
					>
						{t.text}
					</div>
				);
			})}
		</div>
	);
}
