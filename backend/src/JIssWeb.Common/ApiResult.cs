namespace JIssWeb.Common;

public class ApiResult
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }

    public static ApiResult Ok(object? data = null) => new() { Success = true, Data = data };

    public static ApiResult Fail(string message, string? code = null) => new() { Success = false, Message = message, Code = code };
}

public class ApiResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }

    public static ApiResult<T> Ok(T data) => new() { Success = true, Data = data };

    public static ApiResult<T> Fail(string message, string? code = null) => new() { Success = false, Message = message, Code = code };
}
