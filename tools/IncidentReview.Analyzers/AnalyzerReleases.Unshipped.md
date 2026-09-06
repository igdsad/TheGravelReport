; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
IR0001 | Security | Error | Dapper SQL must resolve to compile-time text, including across supported private forwarders.
IR0002 | Reliability | Error | Dapper mutations in Store.Sqlite require an explicit non-null transaction, including CommandDefinition overloads.
IR0003 | Architecture | Error | BuildServiceProvider is restricted to the Host composition root.
