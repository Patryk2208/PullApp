import { useState, useEffect } from 'react';
import { GetUserResponse, IUserRepository } from '@pullapp/domain';

export function useProfile(repository: IUserRepository) {
	const [profile, setProfile] = useState<GetUserResponse | null>(null);
	const [isLoading, setIsLoading] = useState(true);
	const [error, setError] = useState<string | null>(null);
	
	useEffect(() => {
		async function fetchProfile() {
			setIsLoading(true);
			const result = await repository.me();
			console.log("fetchProfile received", result);
			
			if (result.ok) {
				setProfile(result.value);
			} else {
				setError(result.error);
			}
			setIsLoading(false);
		}
		
		fetchProfile();
	}, [repository]);
	
	return { profile, isLoading, error };
}