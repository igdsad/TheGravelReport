using IncidentReview.Domain;

namespace IncidentReview.Store.Contracts;

public sealed record GetSessionBySimulatorKey : IStoreQuery<StoreLookup<StoredSession>>
{
    public GetSessionBySimulatorKey(SimulatorCode simulator, SimulatorSessionKey sessionKey)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        ArgumentNullException.ThrowIfNull(sessionKey);
        Simulator = simulator;
        SessionKey = sessionKey;
    }

    public SimulatorCode Simulator { get; }
    public SimulatorSessionKey SessionKey { get; }
}

public sealed record GetSession : IStoreQuery<StoreLookup<StoredSession>>
{
    public GetSession(SessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
    }

    public SessionIdentity Session { get; }
}

public sealed class ListSessions : IStoreQuery<IReadOnlyList<StoredSessionSummary>>
{
    private ListSessions()
    {
    }

    public static ListSessions Instance { get; } = new();
}

public sealed record GetSessionDetails : IStoreQuery<StoreLookup<StoredSessionDetails>>
{
    public GetSessionDetails(SessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
    }

    public SessionIdentity Session { get; }
}
