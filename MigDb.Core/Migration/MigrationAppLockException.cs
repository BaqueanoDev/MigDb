namespace MigDb.Core.Migration;

/// <summary>
/// Thrown when the sp_getapplock could not be acquired
/// </summary>
public sealed class MigrationAppLockException(int result) : Exception(GetMessage(result))
{
    public int Result { get; } = result;

    private static string GetMessage(int result) => result switch
    {
        -1 => "Timed out waiting for app lock.",
        -2 => "The app lock request was cancelled.",
        -3 => "The app lock request was chosen as a deadlock victim.",
        -999 => "The app lock request failed.",
        _ => $"Failed to acquire the app lock returned {result}.",
    };
}
