namespace Application.Common.Models;

public record ApiResult<T>(T? Value, bool IsSuccess, Error? Error = null)
{
    public static ApiResult<T> Success(T value) => new(value, true, null);
    public static ApiResult<T> Failure(Error error) => new(default, false, error);
}
