export default interface IResponseModel<T> {
    content?: T | null;
    message: string;
    success: boolean;
    errors: Record<string, string>;
}
