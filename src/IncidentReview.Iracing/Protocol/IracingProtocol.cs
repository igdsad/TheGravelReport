namespace IncidentReview.Iracing.Protocol;

internal static class IracingProtocol
{
    public const string DataValidEventName = "Local\\IRSDKDataValidEvent";
    public const string MemoryMapName = "Local\\IRSDKMemMapFileName";
    public const string BroadcastMessageName = "IRSDK_BROADCASTMSG";

    public const int Version = 2;
    public const int ConnectedStatus = 1;
    public const int MaximumBuffers = 4;
    public const int MaximumVariables = 4096;
    public const int MaximumBufferLength = 16 * 1024 * 1024;
    public const int MaximumSessionInfoLength = 16 * 1024 * 1024;
    public const int MaximumCarIndex = 63;
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);

    public const int HeaderSize = 112;
    public const int VariableHeaderSize = 144;
    public const int VariableBufferSize = 16;
    public const int VariableBufferOffset = 48;

    public const int SessionInfoUpdateOffset = 12;
    public const int CurrentBufferOffset = 44;

    public const string SessionNumberVariable = "SessionNum";
    public const string SessionTimeVariable = "SessionTime";
    public const string TeamIncidentCountVariable = "PlayerCarTeamIncidentCount";
    public const string DriverIncidentCountVariable = "PlayerCarDriverIncidentCount";
    public const string LapVariable = "Lap";
    public const string LapDistanceVariable = "LapDistPct";
    public const string IsOnTrackVariable = "IsOnTrack";
    public const string IsReplayPlayingVariable = "IsReplayPlaying";
    public const string ReplaySessionNumberVariable = "ReplaySessionNum";
    public const string ReplaySessionTimeVariable = "ReplaySessionTime";
    public const string ReplayPlaySpeedVariable = "ReplayPlaySpeed";
    public const string ReplayPlaySlowMotionVariable = "ReplayPlaySlowMotion";
    public const string CameraStateVariable = "CamCameraState";
    public const string CameraCarIndexVariable = "CamCarIdx";
    public const string CameraGroupNumberVariable = "CamGroupNumber";
    public const string CameraNumberVariable = "CamCameraNumber";
}

internal enum IracingVariableType
{
    Character = 0,
    Boolean = 1,
    Integer = 2,
    BitField = 3,
    Single = 4,
    Double = 5,
}

internal enum IracingBroadcastMessage
{
    CameraSwitchNumber = 1,
    ReplaySetPlaySpeed = 3,
    ReplaySearchSessionTime = 12,
}

[Flags]
internal enum IracingCameraState
{
    None = 0,
    IsSessionScreen = 0x0001,
}
