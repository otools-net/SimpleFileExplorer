using System.Text;
using Otools.Net.Sfe.Core.Connections.Previews;
using Otools.Net.Sfe.Core.Library;
using Otools.Net.Sfe.Tests.Infrastructure;

namespace Otools.Net.Sfe.Tests;

/// <summary>
/// Files of connections are told media by their bytes, never their names (connections-index plan, attack vector "a
/// remote .html, .svg, playlist or script named as media given to the viewer or ffmpeg").
/// </summary>
[TestClass]
public sealed class MediaSnifferTests
{
    [TestMethod]
    [DataRow("testsrc-1s-320x180.mp4", ItemKind.Video)]
    [DataRow("testsrc-4s-mpeg4-pcm.avi", ItemKind.Video)]
    [DataRow("sine-440hz-2s.wav", ItemKind.Audio)]
    [DataRow("testsrc-64x48-still.png", ItemKind.Image)]
    [DataRow("testsrc-64x48-5frames.apng", ItemKind.Image)]
    [DataRow("testsrc-64x48-still.gif", ItemKind.Image)]
    public void RealMediaIsToldByItsBytes(string sample, ItemKind kind)
    {
        Assert.AreEqual(kind, MediaSniffer.DetectFile(TestSamples.PathOf(sample)));
    }

    [TestMethod]
    [DataRow(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1 })]
    [DataRow(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0, 0, 0, 0, 0, 0, 0x1F })]
    [DataRow(new byte[] { (byte)'I', (byte)'D', (byte)'3', 4, 0, 0, 0, 0, 0, 0x21, 0, 0 })]
    [DataRow(new byte[] { 0xFF, 0xFB, 0x90, 0x64, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void OtherSignaturesAreMediaToo(byte[] head)
    {
        Assert.IsNotNull(MediaSniffer.Detect(head));
    }

    [TestMethod]
    [DataRow("#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:10,\nhttp://127.0.0.1:9/segment.ts\n")]
    [DataRow("ffconcat version 1.0\nfile '../Private/secret.mp4'\n")]
    [DataRow("<html><script>alert(document.cookie)</script></html>")]
    [DataRow("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>")]
    [DataRow("<?xml version=\"1.0\"?><MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\"></MPD>")]
    [DataRow("v=0\no=- 0 0 IN IP4 127.0.0.1\ns=x\nc=IN IP4 127.0.0.1\nm=video 9 RTP/AVP 96\n")]
    [DataRow("LoadPlugin(\"x.dll\")\nDirectShowSource(\"C:\\\\secret.mp4\")\n")]
    public void PlaylistsScriptsAndPagesAreNotMedia(string text)
    {
        Assert.IsNull(MediaSniffer.Detect(Encoding.UTF8.GetBytes(text)));
    }

    [TestMethod]
    public void TextInUtf16IsNotTakenForSound()
    {
        var text = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("#EXTM3U\nhttp://x/a.ts\n"));

        Assert.IsNull(MediaSniffer.Detect(text.ToArray()));
    }

    [TestMethod]
    public void AFewBytesAreNothing()
    {
        Assert.IsNull(MediaSniffer.Detect([0xFF, 0xD8, 0xFF]));
    }
}
