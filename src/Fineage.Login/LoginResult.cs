namespace Fineage.Login;

public sealed class LoginResult<T>
{
    public bool Ok { get; private init; }
    public T? Value { get; private init; }
    public string Code { get; private init; } = "";
    public string Message { get; private init; } = "";

    public static LoginResult<T> Success(T value) => new() { Ok = true, Value = value };

    public static LoginResult<T> Fail(string code, string message) => new() { Code = code, Message = message };

    public LoginResult<TOut> Forward<TOut>() => LoginResult<TOut>.Fail(Code, Message);
}
