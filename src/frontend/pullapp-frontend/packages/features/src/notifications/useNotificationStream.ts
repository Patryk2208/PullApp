import { useEffect, useRef } from 'react';
import { useAuthStore } from '../auth/authStore';

export interface SseEvent {
	type: string;
	data: any;
}

// ── Współdzielony, pojedynczy transport SSE ──────────────────────────────────
// Wszyscy konsumenci (toasty pasażera, panel kierowcy, …) subskrybują JEDEN
// strumień zamiast otwierać własne połączenia. Połączenie wstaje przy pierwszym
// subskrybencie, znika przy ostatnim, i reconnectuje przy zmianie tokena.
type Subscriber = (e: SseEvent) => void;

const subscribers = new Set<Subscriber>();
let activeController: AbortController | null = null;
let activeToken: string | null = null;

function startConnection(token: string) {
	const controller = new AbortController();
	activeController = controller;
	activeToken = token;

	(async () => {
		try {
			const res = await fetch('/api/sse', {
				headers: { Authorization: `Bearer ${token}`, Accept: 'text/event-stream' },
				signal: controller.signal,
			});
			if (!res.ok || !res.body) return;

			const reader = res.body.getReader();
			const decoder = new TextDecoder();
			let buffer = '';
			let eventType = '';

			while (true) {
				const { done, value } = await reader.read();
				if (done) break;
				buffer += decoder.decode(value, { stream: true });
				const lines = buffer.split('\n');
				buffer = lines.pop() ?? '';

				for (const line of lines) {
					if (line.startsWith('event:')) {
						eventType = line.slice(6).trim();
					} else if (line.startsWith('data:')) {
						const raw = line.slice(5).trim();
						let parsed: any = raw;
						try { parsed = JSON.parse(raw); } catch { /* zostaw raw */ }
						const evt: SseEvent = { type: eventType || 'message', data: parsed };
						subscribers.forEach((s) => s(evt));
						eventType = '';
					}
				}
			}
		} catch (err: any) {
			if (err?.name !== 'AbortError') console.warn('notification stream error', err);
		} finally {
			// pozwól na ponowne połączenie, jeśli strumień się zakończył
			if (activeController === controller) {
				activeController = null;
				activeToken = null;
			}
		}
	})();
}

function ensureConnection(token: string) {
	if (activeController && activeToken === token) return;
	if (activeController) activeController.abort();
	startConnection(token);
}

function stopConnection() {
	activeController?.abort();
	activeController = null;
	activeToken = null;
}

/**
 * Subskrybuje współdzielony strumień powiadomień SSE. `onEvent` woła się dla
 * każdego zdarzenia. Połączenie jest współdzielone między wszystkimi hookami.
 */
export function useNotificationStream(onEvent: Subscriber) {
	const token = useAuthStore((s) => s.token);
	const onEventRef = useRef(onEvent);
	onEventRef.current = onEvent;

	useEffect(() => {
		if (!token) return;
		const sub: Subscriber = (e) => onEventRef.current(e);
		subscribers.add(sub);
		ensureConnection(token);

		return () => {
			subscribers.delete(sub);
			if (subscribers.size === 0) stopConnection();
		};
	}, [token]);
}
