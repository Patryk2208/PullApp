// packages/api-client/src/apiClient.ts
import axios from 'axios';

let tokenProvider: () => string | null = () => null;
let onUnauthorizedHandler: () => void = () => {};

export const registerTokenProvider = (provider: () => string | null) => {
	tokenProvider = provider;
};
export const registerUnauthorizedHandler = (handler: () => void) => {
	onUnauthorizedHandler = handler;
};

export const publicApiClient = axios.create({
	headers: { 'Content-Type': 'application/json' },
});
export const authenticatedApiClient = axios.create({
	headers: { 'Content-Type': 'application/json' },
});

// REQUEST INTERCEPTOR: dokleja do requestów token pobrany przez bezpieczny provider
authenticatedApiClient.interceptors.request.use(
	(config) => {
		const token = tokenProvider();
		if (token && config.headers) {
			config.headers.Authorization = `Bearer ${token}`;
		}
		return config;
	},
	(error) => Promise.reject(error)
);

// RESPONSE INTERCEPTOR: reaguje na 401 Unauthorized za pomocą wstrzykniętego handlera
authenticatedApiClient.interceptors.response.use(
	(response) => response,
	(error) => {
		if (error.response && error.response.status === 401) {
			onUnauthorizedHandler();
		}
		return Promise.reject(error);
	}
);