import { authenticatedApiClient } from '../apiClient';
import {err, GetUserQuery, GetUserResponse, IUserRepository, ok, Result} from '@pullapp/domain';

export class UserRepository implements IUserRepository {
	constructor(baseUrl: string) {
		authenticatedApiClient.defaults.baseURL = baseUrl.replace(/\/$/, '');
	}
	
	async me(): Promise<Result<GetUserResponse>> {
		try {
			const response = await authenticatedApiClient.get<GetUserResponse>('/api/users/me');
			console.log("UserRepository received", response);
			return ok(response.data);
		} catch (error: any) {
			console.log("UserRepository received", error);
			if (error.response && error.response.data) {
				return err(error.response.data.detail || 'Nie udało się pobrać profilu');
			}
			return err('Brak połączenia z serwerem');
		}
	}
}