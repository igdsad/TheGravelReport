using IncidentReview.Results;

namespace IncidentReview.Replay.Contracts;

/// <summary>Reads the latest immutable replay context without issuing simulator commands.</summary>
public interface IReplayContextReader
{
    /// <summary>Reads the context most recently reported by the connected simulator.</summary>
    public Result<ReplayContext> Read();
}
