import { useState } from 'react';
import type { IAuthRepository, LoginUserCommand } from '@pullapp/domain';
import { useAuthStore } from './authStore';

export function useLogin(repository: IAuthRepository) {
	const setToken = useAuthStore((s) => s.setToken);
	const [isLoading, setIsLoading] = useState(false);
	const [error,     setError]     = useState<string | null>(null);
	
	async function login(credentials: LoginUserCommand): Promise<boolean> {
		setIsLoading(true);
		setError(null);
		
		const result = await repository.login(credentials);
		console.log('login receives', result);
		
		if (result.ok) {
			console.log('login calls setToken', result.value.token);
			setToken(result.value.token);
		} else {
			setError(result.error);	
		}
		
		setIsLoading(false);
		return result.ok;
	}
	
	return { login, isLoading, error };
}
