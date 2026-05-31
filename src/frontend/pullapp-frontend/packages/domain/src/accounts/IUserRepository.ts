import {GetUserQuery, GetUserResponse} from "./models";
import {Result} from "../shared/result";

export interface IUserRepository {
	me(): Promise<Result<GetUserResponse>>;
}