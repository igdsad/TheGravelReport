namespace IncidentReview.Store.Sqlite;

internal static class SqliteNativeProvider
{
    private static readonly object Sync = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized))
        {
            return;
        }

        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            Volatile.Write(ref _initialized, true);
        }
    }
}
