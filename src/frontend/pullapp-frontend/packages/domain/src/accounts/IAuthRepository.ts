import { Result } from "../shared/result";
import {
    LoginUserCommand,
    RegisterUserCommand,
    LoginUserResponse,
    RegisterUserResponse
} from './models';

export interface IAuthRepository {
    login(credentials: LoginUserCommand): Promise<Result<LoginUserResponse>>;
    register(credentials: RegisterUserCommand): Promise<Result<RegisterUserResponse>>;
}
