namespace AidlyPms.Api.Common;

public class PosSaleException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }
    public string ErrorMessage => Message;

    public PosSaleException(string errorCode, string message, int statusCode = 400)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}
