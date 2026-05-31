export interface LoginUserCommand {
    email: string;
    password: string;
}

export interface LoginUserResponse {
    token: string;
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

export interface RegisterUserResponse {
	userId: number;
}

export interface GetUserQuery {
	email: string;
}

export interface GetUserResponse {
	id: number;
	name: string;
	surname: string;
	email: string;
	profilePicture: string | null;
	birthDate: string;
	bio: string;
	role: UserRole;
}

enum UserRole {
	regularUser = 1,
	admin = 2,
}