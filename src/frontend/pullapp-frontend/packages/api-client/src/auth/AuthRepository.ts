import type {
	IAuthRepository,
	LoginUserCommand, LoginUserResponse,
	RegisterUserCommand, RegisterUserResponse } from '@pullapp/domain';
import { ok, err, Result } from '@pullapp/domain';

// TODO
const BASE_URL = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5000';

async function post<T>(path: string, body: unknown): Promise<Result<T>> {
	try {
		const res = await fetch(`${BASE_URL}${path}`, {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify(body),
		});
		if (!res.ok) {
			const message = await res.text();
			return err(message || res.statusText);
		}
		return ok(await res.json() as T);
	} catch {
		return err('Brak połączenia z serwerem');
	}
}

export class AuthRepository implements IAuthRepository {
	login(credentials: LoginUserCommand): Promise<Result<LoginUserResponse>> {
		return post('/api/auth/login', credentials);
	}
	register(credentials: RegisterUserCommand): Promise<Result<RegisterUserResponse>> {
		return post('/api/auth/register', credentials);
	}
}