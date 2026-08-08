# Direct SDS publication contract

This document defines confirmed intended behavior for resolving the SDS connector, uploading one complete dataset, and validating it. Current mismatches are listed by ID in [the deviation register](../DEVIATIONS.md).

## Connector resolution and dataset format

`SchoolDataSync:SourceName` is a required non-empty SDS source name. Retrieve the Graph data-connector collection through Microsoft Graph beta:

```text
GET https://graph.microsoft.com/beta/external/industryData/dataConnectors
```

The response `value` collection must contain exactly one connector with a `displayName` that matches `SourceName` exactly. That connector must have a non-empty UUID `id`, `@odata.type` equal to `#microsoft.graph.industryData.azureDataLakeConnector`, and one supported `fileFormat.code`:

| Connector code | Dataset |
|---|---|
| `schoolDataSyncV1` | SDS V1 |
| `schoolDataSyncV2Rev1` | SDS V2.1 |

The returned `id` is the `ConnectorId`. `ConnectorId` is not a configuration setting. A missing, duplicate, malformed, unsupported, or unsuccessful connector response stops the run before Somtoday data is uploaded.

Microsoft Graph Industry Data APIs used here are available only under `/beta`, can change without notice, and are not supported by Microsoft for production use. Operators accept this platform risk when deploying the application.

The runtime identity has `IndustryData-DataConnector.Read.All` to resolve the named connector, `IndustryData-DataConnector.Upload` for the upload session and validation action, and `IndustryData.ReadBasic.All` for reading the validation operation returned in `Location`. This follows Microsoft's automated CSV upload permission model while retaining the additional operation-read permission required by this application's validation polling.

## One publication unit per run

One Job run builds at most one dataset, in the version selected by the connector. All successfully resolved Somtoday institutions and their eligible selected locations are aggregated in configuration order.

Institution discovery or download failures are isolated: the failed institution is omitted, successfully resolved institutions remain eligible for the combined dataset, and the final process exit code is `1`. If no institution remains, no upload session is requested. Normal mode also skips publication without failure when the successful population contains no exportable location. Header-only mode builds the selected format's complete header-only set after at least one institution was discovered successfully.

Serialize the complete dataset to memory before requesting an upload session. Guardian files are present when guardian sync is enabled, including when they contain headers only. A serialization or conversion error results in no upload session and a nonzero exit code.

## Upload session and SAS URI

Request exactly one new upload session for the dataset:

```text
GET https://graph.microsoft.com/beta/external/industryData/dataConnectors/{ConnectorId}/microsoft.graph.industryData.azureDataLakeConnector/getUploadSession?resetSession=true
```

The response must be `200 OK` and contain an absolute HTTPS `sessionUrl` with a non-empty querystring. The URL and every value derived from it are secrets and must never be logged.

For each dataset file, append one URL-escaped file-name segment to the session container path before the original `?`. Preserve the original SAS querystring exactly, including ordering and escaping. Reject empty file names, nested paths, fragments, non-HTTPS URLs, or URLs without a SAS querystring.

## Complete BlockBlob upload

Upload files sequentially in the dataset's defined order. Use the Azure Storage `Put Blob` REST operation for one complete BlockBlob:

```text
PUT {session-container}/{escaped-file-name}?{original-sas-query}
```

Each request contains exactly the serialized CSV bytes and these headers:

- `Content-Type: application/vnd.ms-excel`
- `Content-Length: <exact byte count>`
- `x-ms-version: 2023-11-03`
- `x-ms-blob-content-type: application/vnd.ms-excel`
- `x-ms-blob-type: BlockBlob`
- `x-ms-meta-uploadvia: PortalUpload`

Only `201 Created` is success. Stop after the first failed file and do not call validation. The temporary SDS-owned container expires according to the upload-session response; the application does not delete, promote, version, retain, or roll back its contents.

## Validation

After every file returns `201 Created`, start validation with no request content or JSON body:

```text
POST https://graph.microsoft.com/beta/external/industryData/dataConnectors/{ConnectorId}/validate
```

Require `202 Accepted` and an absolute HTTPS `Location` URL hosted by `graph.microsoft.com`. Poll that URL with authenticated `GET` requests no more frequently than every five seconds and stop after thirty minutes.

Interpret validation status case-insensitively:

- `notStarted` and `running`: continue polling;
- `succeeded`: success;
- `failed`, `unknownFutureValue`, missing values, and any unrecognized future value: failure.

Validation errors and warnings can contain source detail. Log only safe counts and the terminal status, never response bodies, status detail, error objects, warning objects, validated file collections, or resource URLs.

## Retry, timeout, and failure behavior

Graph calls, SAS uploads, and validation polls get at most four total HTTP attempts per request. The Graph and SAS clients do not follow redirects. Treat every 3xx response as a permanent protocol failure. Retry only network/HTTP timeout failures, HTTP 408, HTTP 429, and HTTP 5xx. Do not retry other 4xx, malformed payloads, or protocol failures.

Before a retry, use `Retry-After` as either delta-seconds or an HTTP date. Without a valid value, wait two seconds, except during validation polling. Consecutive validation polling attempts remain at least five seconds apart: use five seconds when `Retry-After` is missing or shorter, and retain a longer value. Every request and delay preserves application cancellation. The thirty-minute validation deadline also cancels polling and retry delays.

Connector, upload-session, file-upload, validation-start, validation-poll, timeout, and cancellation failures produce process exit code `1`. Safe logs may contain endpoint operation names, file names, attempt counts, numeric HTTP status, connector identifiers, and exception types; they must not contain authorization headers, access tokens, Somtoday secrets, SAS material, response bodies, CSV values, or personal identifiers.
