# IncidentReview repository analyzers

The analyzer rules are build-time enforcement only. They are attached as analyzers and never become runtime dependencies of production assemblies.

## IR0001: Parameterize runtime SQL values

SQL passed to a Dapper `Query*` or `Execute*` API must not use interpolation or runtime string concatenation. Put runtime values in Dapper parameters instead.

```csharp
// Rejected
connection.Query<Row>($"SELECT * FROM Incident WHERE Id = {incidentId}");

// Accepted
connection.Query<Row>(
    "SELECT * FROM Incident WHERE Id = @IncidentId",
    new { IncidentId = incidentId });
```

Compile-time constant concatenation is accepted because it introduces no runtime value. Dynamic identifiers remain subject to the separate code-owned allowlist requirement.

## IR0002: Supply the active transaction

Every Dapper `Execute*` call compiled into `IncidentReview.Store.Sqlite` must explicitly receive a non-null `transaction` argument. The rule treats all `Execute*` APIs as mutation-capable and reports omitted, `null`, and `default` arguments.

```csharp
// Rejected
connection.Execute(sql, values);

// Accepted
connection.Execute(sql, values, transaction: transaction);
```

The analyzer verifies the call shape. Runtime ownership and lifetime of the transaction remain covered by store contract and integration tests.

## IR0003: Do not build nested service providers

Calls to Microsoft's `IServiceCollection.BuildServiceProvider` extension are rejected outside the `IncidentReview.Host.Wpf` composition-root assembly. Registration libraries add services to the supplied collection and allow the host to build the single provider.

```csharp
// Rejected in a library registration method
using var provider = services.BuildServiceProvider();
```

This is an assembly-boundary rule. The host's wiring and lifetime behavior remain covered by composition tests and code review.
