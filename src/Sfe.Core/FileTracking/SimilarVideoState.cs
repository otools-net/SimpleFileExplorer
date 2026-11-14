namespace Otools.Net.Sfe.Core.FileTracking;

public enum SimilarVideoState
{
    /// <summary>No comparison since the application started.</summary>
    Idle,
    Running,
    Completed,

    /// <summary>Stopped from the "Background tasks" dialog.</summary>
    Cancelled,
    Failed,
}
