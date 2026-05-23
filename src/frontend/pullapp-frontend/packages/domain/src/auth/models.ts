export interface LoginUserResponse {
    token: string;
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
    birthDate: Date;
}
