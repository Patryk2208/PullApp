export interface LoginUserResponse {
    accessToken: string;
}

export interface LoginUserCommand {
    email: string;
    password: string;
}

export interface RegisterUserResponse {
    userId: number;
}

export interface RegisterUserCommand {
    name: string;
    surname: string;
    email: string;
    password: string;
	
	// `Date` serializowało się w stylu "2000-01-01T00:00:00.000Z",
	// co było odrzucane przez back-end oczekujący `DateOnly`
    birthDate: string;
}
