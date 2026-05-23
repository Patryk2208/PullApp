import { useState } from 'react';
import type { IAuthRepository, RegisterUserCommand } from '@pullapp/domain';

export function useRegister(repository: IAuthRepository) {
	const [isLoading, setIsLoading] = useState(false);
	const [error, setError]         = useState<string | null>(null);
	const [userId, setUserId]       = useState<number | null>(null);
	
	async function register(credentials: RegisterUserCommand) {
		setIsLoading(true);
		setError(null);
		
		const result = await repository.register(credentials);
		
		if (result.ok) {
			setUserId(result.value.userId);
		} else {
			setError(result.error);
		}
		
		setIsLoading(false);
	}
	
	return { register, isLoading, error, userId };
}