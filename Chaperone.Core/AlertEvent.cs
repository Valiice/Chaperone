namespace Chaperone.Core;

public readonly record struct AlertEvent(
    PlayerKey Targeter,
    string TargeterName,
    uint TargeterWorldId,
    PlayerKey Subject,
    string SubjectName,
    uint SubjectWorldId,
    double DwellSeconds);
