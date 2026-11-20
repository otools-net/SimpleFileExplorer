/**
 * Downloads the frame `video` shows at the video's own resolution, as a PNG named `fileName`, through a link added to
 * `host` for the click. The video comes from this server, so the canvas isn't tainted; a blob link with `download`
 * works on plain-http LAN pages, unlike the image clipboard. Returns false when no frame is decoded yet.
 */
export function downloadFrame(video: HTMLVideoElement, fileName: string, host: HTMLElement): boolean {
    if (!video.videoWidth || video.readyState < 2) return false;
    const canvas = Object.assign(document.createElement('canvas'), {
        width: video.videoWidth,
        height: video.videoHeight,
    });
    canvas.getContext('2d')!.drawImage(video, 0, 0);
    canvas.toBlob((blob) => {
        if (!blob) return;
        const url = URL.createObjectURL(blob);
        const link = Object.assign(document.createElement('a'), { href: url, download: fileName });
        host.append(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 10000);
    }, 'image/png');
    return true;
}
