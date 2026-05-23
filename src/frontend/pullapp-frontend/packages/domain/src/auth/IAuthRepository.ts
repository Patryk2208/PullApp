import {
    LoginUserCommand,
    RegisterUserCommand,
    LoginUserResponse,
    RegisterUserResponse
} from './models';

export interface IAuthRepository {
    login(credentials: LoginUserCommand): Promise<LoginUserResponse>;
    register(credentials: RegisterUserCommand): Promise<RegisterUserResponse>;
}
