namespace Chaperone.Core;

[Flags]
public enum ObservationFlags : byte
{
    None = 0,
    Party = 1,
    Alliance = 2,
    Friend = 4,
    FreeCompany = 8,
    InCombat = 16,
    LocalPlayer = 32,
}
