namespace Ash.SharedKernel.Concurrency;

public sealed class RowVersionMismatchException : Exception
{
    public RowVersionMismatchException()
        : base("The record was modified by someone else.")
    {
    }

    public RowVersionMismatchException(string message) : base(message)
    {
    }

    public RowVersionMismatchException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
