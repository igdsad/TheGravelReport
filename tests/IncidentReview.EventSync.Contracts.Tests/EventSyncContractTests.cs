using IncidentReview.Domain;
using IncidentReview.Results;

namespace IncidentReview.EventSync.Contracts.Tests;

[TestClass]
public sealed class EventSyncContractTests
{
    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    public void JoinCodeMatchesTheVersionOneGoldenVector()
    {
        const string expected =
            "grv1_AQAjaHR0cDovLzEyNy4wLjAuMTo1MDg4L2xlYWd1ZS1uaWdodC8h9_jegFFbiYaAAZXveYtq";
        var session = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");

        var created = JoinCode.TryCreate(
            new Uri("http://127.0.0.1:5088/league-night/"),
            session);
        var parsed = JoinCode.TryParse(expected);

        Assert.IsTrue(created.IsSuccess);
        Assert.AreEqual(expected, created.Value.Value);
        Assert.IsTrue(parsed.IsSuccess);
        Assert.AreEqual("http://127.0.0.1:5088/league-night/", parsed.Value.ServerBaseUri.AbsoluteUri);
        Assert.AreEqual(session, parsed.Value.SessionIdentity);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    public void JoinCodeRoundTripsCanonicalServerAndSessionDeterministically()
    {
        var session = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
        var first = JoinCode.TryCreate(
            new Uri("https://review.example.test/league-night"),
            session);
        var second = JoinCode.TryCreate(
            new Uri("https://review.example.test/league-night/"),
            session);

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsSuccess);
        Assert.AreEqual(1, JoinCode.CurrentVersion);
        Assert.AreEqual(first.Value.Value, second.Value.Value);
        Assert.AreEqual(
            "https://review.example.test/league-night/",
            first.Value.ServerBaseUri.AbsoluteUri);

        var parsed = JoinCode.TryParse(first.Value.Value);

        Assert.IsTrue(parsed.IsSuccess);
        Assert.AreEqual(first.Value, parsed.Value);
        Assert.AreEqual(first.Value.Value, parsed.Value.ToString());
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    [DataRow("empty")]
    [DataRow("unsupported-scheme")]
    [DataRow("credentials")]
    [DataRow("query")]
    [DataRow("fragment")]
    [DataRow("provisional-session")]
    [DataRow("malformed-code")]
    public void JoinCodeRejectsEachInvalidInputDimension(string dimension)
    {
        var session = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
        Result<JoinCode> result;
        switch (dimension)
        {
            case "empty":
                result = JoinCode.TryParse(string.Empty);
                break;
            case "unsupported-scheme":
                result = JoinCode.TryCreate(new Uri("ftp://review.example.test/"), session);
                break;
            case "credentials":
                result = JoinCode.TryCreate(new Uri("https://user@review.example.test/"), session);
                break;
            case "query":
                result = JoinCode.TryCreate(new Uri("https://review.example.test/?x=1"), session);
                break;
            case "fragment":
                result = JoinCode.TryCreate(new Uri("https://review.example.test/#x"), session);
                break;
            case "provisional-session":
                result = JoinCode.TryCreate(
                    new Uri("https://review.example.test/"),
                    SessionIdentity.Generate());
                break;
            case "malformed-code":
                var valid = JoinCode.TryCreate(
                    new Uri("https://review.example.test/"),
                    session).Value.Value;
                result = JoinCode.TryParse(valid + "!");
                break;
            default:
                Assert.Fail($"Unknown dimension: {dimension}");
                return;
        }

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EventSyncErrorCodes.InvalidJoinCode, result.Error?.Code);
        Assert.AreEqual(ErrorKind.Validation, result.Error?.Kind);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-002")]
    public void SubmissionRequiresItsDeterministicIdentityToMatchReplayPosition()
    {
        var session = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");
        var firstPosition = CreatePosition(sessionNumber: 1, milliseconds: 12_345);
        var secondPosition = CreatePosition(sessionNumber: 1, milliseconds: 12_346);
        var eventId = CustomEventId.CreateDeterministic(session, firstPosition);
        var submitter = SubmitterName.TryCreate("Driver One").Value;
        var occurredAt = UtcInstant.TryCreateUnixMilliseconds(1_800_000_000_000).Value;

        var valid = CustomEventSubmission.TryCreate(
            eventId,
            session,
            firstPosition,
            submitter,
            occurredAt);
        var mismatch = CustomEventSubmission.TryCreate(
            eventId,
            session,
            secondPosition,
            submitter,
            occurredAt);

        Assert.IsTrue(valid.IsSuccess);
        Assert.AreEqual(eventId, valid.Value.Id);
        Assert.AreEqual(submitter, valid.Value.Submitter);
        Assert.IsFalse(mismatch.IsSuccess);
        Assert.AreEqual(EventSyncErrorCodes.InvalidSubmission, mismatch.Error?.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "IR-SYNC-001")]
    public void HostRequestKeepsListenAndAdvertisedAddressesSeparate()
    {
        var session = CreateSession("21f7f8de-8051-5b89-8680-0195ef798b6a");

        var result = CustomEventSessionHostRequest.TryCreate(
            session,
            new Uri("http://127.0.0.1:47321"),
            new Uri("https://review.example.test/league-night"));
        var invalidListenPath = CustomEventSessionHostRequest.TryCreate(
            session,
            new Uri("http://127.0.0.1:47321/not-root"),
            new Uri("https://review.example.test/"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("http://127.0.0.1:47321/", result.Value.ListenUri.AbsoluteUri);
        Assert.AreEqual(
            "https://review.example.test/league-night/",
            result.Value.AdvertisedBaseUri.AbsoluteUri);
        Assert.IsFalse(invalidListenPath.IsSuccess);
        Assert.AreEqual(EventSyncErrorCodes.InvalidHostRequest, invalidListenPath.Error?.Code);
    }

    [TestMethod]
    [TestProperty("Requirement", "QR-ARC-002")]
    public void ContractValuesHaveNoPublicConstructorsOrMutableProperties()
    {
        Type[] contractValues =
        [
            typeof(JoinCode),
            typeof(CustomEventSubmission),
            typeof(CustomEventSessionHostRequest),
        ];

        foreach (var type in contractValues)
        {
            Assert.IsTrue(type.IsSealed, type.FullName);
            Assert.IsEmpty(type.GetConstructors(), type.FullName);
            Assert.IsFalse(
                type.GetProperties().Any(static property => property.SetMethod is not null),
                type.FullName);
        }
    }

    private static SessionIdentity CreateSession(string value) =>
        SessionIdentity.TryCreate(Guid.ParseExact(value, "D")).Value;

    private static ReplayPosition CreatePosition(int sessionNumber, long milliseconds) =>
        ReplayPosition.TryCreate(
            SessionNumber.TryCreate(sessionNumber).Value,
            SessionTime.TryCreateMilliseconds(milliseconds).Value).Value;
}
