; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
IR0001 | Security | Error | Dapper SQL must not use runtime interpolation or concatenation.
IR0002 | Reliability | Error | Dapper mutations in Store.Sqlite require an explicit non-null transaction.
IR0003 | Architecture | Error | BuildServiceProvider is restricted to the Host composition root.
