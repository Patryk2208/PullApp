import type {
	IAuthRepository,
	LoginUserCommand, LoginUserResponse,
	RegisterUserCommand, RegisterUserResponse } from '@pullapp/domain';
import { ok, err, Result } from '@pullapp/domain';
import { apiClient } from '../apiClient';

export class AuthRepository implements IAuthRepository {
	
	private _baseUrl: string;
	constructor(baseUrl: string) {
		this._baseUrl = baseUrl.replace(/\/$/, '');
	}
	
	private async _post<T>(path: string, body: unknown): Promise<Result<T>> {
		try {
			const res = await fetch(`${this._baseUrl}${path}`, {
				method: 'POST',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify(body),
			});
			if (res.ok) {
				return ok(await res.json() as T);
			}
			
			const rawText = await res.text();
			try {
				const problem = JSON.parse(rawText);
				const errorMessage = problem.detail || problem.title || res.statusText;
				return err(errorMessage);
			} catch (parsingError) {
				return err(rawText || res.statusText || 'Nieznany błąd serwera');
			}
		} catch {
			return err('Brak połączenia z serwerem');
		}
	}
	
	public login(credentials: LoginUserCommand): Promise<Result<LoginUserResponse>> {
		return this._post('/api/auth/login', credentials);
	}
	public register(credentials: RegisterUserCommand): Promise<Result<RegisterUserResponse>> {
		return this._post('/api/auth/register', credentials);
	}
}