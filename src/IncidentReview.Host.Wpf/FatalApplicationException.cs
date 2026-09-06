using IncidentReview.Results;

namespace IncidentReview.Host.Wpf;

internal sealed class FatalApplicationException : Exception
{
    public FatalApplicationException(Error error)
        : base(error.Message)
    {
        Error = error;
    }

    public Error Error { get; }
}
