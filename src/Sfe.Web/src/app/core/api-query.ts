import { HttpHeaders, HttpParams } from '@angular/common/http';

/** A value of a request's query. */
export type QueryValue = string | number | boolean | null | undefined;

/** Leaves out empty values (and `false`), as the API's defaults apply then. */
export function params(values: Record<string, QueryValue>): HttpParams {
    let result = new HttpParams();
    for (const [key, value] of Object.entries(values)) {
        if (value !== undefined && value !== null && value !== '' && value !== false)
            result = result.set(key, String(value));
    }
    return result;
}

/** `url` with the query of `values` (for requests with a body), nothing added when they are all empty. */
export function withQuery(url: string, values: Record<string, QueryValue>): string {
    const query = params(values).toString();
    return query ? `${url}?${query}` : url;
}

/** A chunk of an upload and its `Content-Digest` (`files/chunk-digest.ts`), which the server checks. */
export interface UploadChunk {
    bytes: Blob;
    digest: string;
}

/** The headers of an upload's chunk: raw bytes, and the digest the server checks them against. */
export function chunkHeaders(headers: HttpHeaders, digest: string): HttpHeaders {
    return headers.set('Content-Type', 'application/octet-stream').set('Content-Digest', digest);
}
