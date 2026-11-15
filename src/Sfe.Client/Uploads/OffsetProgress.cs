using System;

namespace Otools.Net.Sfe.Client.Uploads;

/// <summary>
/// The progress of one file of several, told to <paramref name="total"/> as the bytes of the files before it plus
/// its own.
/// </summary>
internal sealed class OffsetProgress(IProgress<long> total, long before) : IProgress<long>
{
    public void Report(long value) => total.Report(before + value);
}
