# Export contract

This document defines confirmed intended SDS transformation behavior. External SDS constraints and current implementation references are labelled separately. Current mismatches are listed only by ID in [the deviation register](../DEVIATIONS.md).

## Dataset formats and availability

- For every eligible normal-mode output scope, the application always attempts one SDS V1 dataset and one SDS V2.1 dataset from the same included population. Neither version can be disabled or excluded by configuration or command-line selection. The datasets are separate publication units only for failure isolation, so failure of one does not suppress the attempt for the other.
- Both versions use the same included institutions, locations, classes, teachers, pupils, and guardians.
- Normal mode includes a location only when its resolved population contains at least one exportable class. In a grouped dataset, other included locations are still published; the publication unit is skipped only when no location remains. A skip logs a warning, is not a run failure, and leaves existing output unchanged.
- Header-only mode emits all required files with headers and no data rows. It is enabled by `Output:GenerateEmptyCsv`, `--empty-csv`, or automatically on July 31 in `Europe/Amsterdam` time.

## CSV wire contract

Every file is comma-delimited UTF-8 without a byte-order mark. File names and headers are case-sensitive and remain exactly as follows:

| SDS format | File | Headers in order |
|---|---|---|
| V1 | `School.csv` | `SIS ID`, `Name` |
| V1 | `Section.csv` | `SIS ID`, `School SIS ID`, `Section Name`, `Section Number`, `Course Name`, `Course Description` |
| V1 | `Teacher.csv` | `SIS ID`, `School SIS ID`, `Username` |
| V1 | `Student.csv` | `SIS ID`, `School SIS ID`, `Username` |
| V1 | `TeacherRoster.csv` | `Section SIS ID`, `SIS ID` |
| V1 | `StudentEnrollment.csv` | `Section SIS ID`, `SIS ID` |
| V1 guardian | `User.csv` | `Email`, `First Name`, `Last Name`, `Phone`, `SIS ID` |
| V1 guardian | `Guardianrelationship.csv` | `SIS ID`, `Email`, `Role` |
| V2.1 | `orgs.csv` | `sourcedId`, `name`, `type`, `parentSourcedId` |
| V2.1 | `users.csv` | `sourcedId`, `username`, `givenName`, `familyName`, `password`, `activeDirectoryMatchId`, `email`, `phone`, `sms` |
| V2.1 | `roles.csv` | `userSourcedId`, `orgSourcedId`, `role` |
| V2.1 | `classes.csv` | `sourcedId`, `orgSourcedId`, `title`, `sessionSourcedIds`, `courseSourcedId` |
| V2.1 | `enrollments.csv` | `classSourcedId`, `userSourcedId`, `role` |
| V2.1 guardian | `relationships.csv` | `userSourcedId`, `relationshipUserSourcedId`, `relationshipRole` |

The established role values are V1 guardian `guardian`; V2.1 organization roles `staff`, `student`, and guardian `other`; V2.1 enrollment roles `teacher` and `student`; and V2.1 contact relationship role `guardian`.

Before a row is serialized, every string field mapped by that file's active CSV map is checked for carriage-return or line-feed characters. A violation fails dataset construction with an `InvalidDataException` that identifies only the SDS version, exact file name, and exact column header; it never includes the field value or a person identifier. Header-only files and unmapped properties add no such validation failure. Because complete dataset construction precedes staging inside each version boundary, a violation causes zero staging uploads for that version, leaves its live output unchanged, and does not suppress another version or later scope.

Teacher and pupil output is matching-only. V1 deliberately has no name columns in `Teacher.csv` or `Student.csv`; V2.1 leaves their `givenName` and `familyName` values empty. Configure SDS not to create unmatched users. Guardian names remain populated as specified below.

## Class and person population

A class is exportable only when its name is not null, empty, or whitespace and at least one teacher and one pupil remain after reference resolution. Resolve `Lesgroep.Docenten` and `Lesgroep.Leerlingen` UUIDs against the employees and pupils downloaded for the same location. Treat missing reference collections as empty and ignore UUIDs that do not resolve. Teacher-only classes, pupil-only classes, and effectively empty classes are excluded.

Only teachers and pupils belonging to an included class are exported. A person excluded from all classes is absent from every output file, including organization, role, enrollment, and user-related output.

Username generation happens after population resolution. An empty generated username does not change class eligibility.

School-year calculations use an August boundary and `Europe/Amsterdam` time.

## Grouped dataset identities

The existing class-identifier calculations remain unchanged. Both versions emit the filtered class name and append the Amsterdam school year. V1 decides whether to prepend the lower-cased `Vestiging.Afkorting` by comparing the unfiltered `Lesgroep.Naam` with the location abbreviation; V2.1 performs that comparison against the filtered class name. Compare the completed identifiers case-insensitively within each SDS-version dataset. If two different `Lesgroep` UUIDs produce the same identifier, fail the affected SDS-version publication unit instead of changing either identifier or publishing duplicate classes.

When one Somtoday person UUID occurs in multiple included locations:

- V1 emits one teacher or pupil row per location, retaining the same person `SIS ID` and that location's `School SIS ID`.
- V2.1 emits one user row per Somtoday UUID and retains the distinct organization roles.
- V1 guardian rows and V2.1 guardian users are deduplicated by Somtoday UUID.

This behavior assumes that a recurring person UUID represents the same Somtoday source object in every location; only the V1 school association varies per location.

Emit each exact relationship, role, roster, and enrollment combination once. Different Somtoday UUIDs that produce the same username or email remain separate source people and are passed through for Microsoft SDS to validate.

## Username rules

Teacher and pupil username formats are separate configurable expression rules. They allow a school administrator to reproduce an IDM username or email-generation rule when that generated value differs from the value stored in Somtoday. Direct properties, literal text, multiple property expressions, and supported Dynamic LINQ operations are part of the intended configuration surface.

Operational syntax and examples are in the [configuration guide](../operations/CONFIGURATION.md).

## Guardian inclusion and mapping

Guardian sync is optional. Evaluate contact eligibility, relevant pupil relationships, and name eligibility separately. A guardian is exportable only when `WenstContactViaEMail` is `true`, the required email is not null, empty, or whitespace, at least one relationship to an included pupil remains, and both trimmed `Voorletters` and trimmed `Achternaam` are non-empty. Relationships to excluded pupils are omitted. A rejected guardian is omitted consistently from V1 `User.csv` and `Guardianrelationship.csv` and V2.1 `users.csv`, `roles.csv`, and `relationships.csv`.

The mapping is:

| SDS value | Somtoday source |
|---|---|
| V1 `First Name`; V2.1 `givenName` | `OuderVerzorger.Voorletters.Trim()` |
| V1 `Last Name`; V2.1 `familyName` | Optional trimmed `Voorvoegsel` plus required trimmed `Achternaam`, joined with exactly one space |
| V1 `Email`; V2.1 `email` and `username` | `Emailadres` |
| Phone | Selected phone value normalized to E.164 |

Determine the source of fallback `OuderVerzorger.Telefoonnummer` by comparing it with the explicit home, mobile, and work values, then apply the corresponding secret-number flag. Secret numbers are never exported. When multiple permitted values exist, preference is mobile, then home, then work.

Phone preprocessing deliberately remains permissive: retain digits and plus signs while removing other characters, add `0` to a prefixless value of exactly nine digits, convert `00...` to `+...`, convert Dutch `0...` to `+31...`, and preserve an already international `+...` value. Do not reject a source merely because it contained letters or formatting characters before this processing. Both a Dutch number with its national trunk prefix and a nine-digit number without trunk or country prefix are treated as Dutch: `0612345678` and `612345678` both become `+31612345678`.

A non-empty exported phone value must ultimately consist of exactly one leading `+`, followed by 2 through 15 ASCII digits; the first digit must be `1` through `9`. Emit an empty phone value when the processed result does not meet this rule. An invalid optional phone does not exclude the guardian or any otherwise valid relationship, and source values are never logged.

The resolved population records, per location, how many guardians passed contact and pupil-relationship eligibility but were excluded only for missing required name fields. Log that count once for the location without logging a name, UUID, email address, or phone number.

When guardian sync is enabled but generates no guardians or relationships, guardian-specific files are still emitted with headers only. Published-file removal when guardian sync is disabled is defined by the [publication contract](PUBLICATION.md).

Somtoday does not indicate whether an adult pupil consented to guardian access. Keep the weekly guardian summary in Microsoft 365 disabled unless the school has independently established that enabling it is appropriate, and complete a privacy assessment before using guardian sync.

## External SDS constraints

- Microsoft documents V1 teacher and pupil `SIS ID` as a unique ID. The confirmed per-location V1 behavior above can deliberately repeat that value when one person belongs to multiple locations and may therefore be rejected by SDS.
- [SDS V1 CSV format](https://learn.microsoft.com/en-us/schooldatasync/sds-v1-csv-file-format) requires `user.csv` to hold separate guardian `Email`, `First Name`, and `Last Name` values; `Phone` and `SIS ID` are optional, and phone is E.164 formatted.
- [SDS V2.1 CSV format](https://learn.microsoft.com/en-us/schooldatasync/sds-v2.1-csv-file-format) represents a guardian in `users.csv` with `sourcedId`, `username`, `givenName`, `familyName`, `email`, and optional `phone`.
- [Microsoft guardian guidance](https://learn.microsoft.com/en-us/schooldatasync/parents-and-guardians-in-sds) requires names and email for a user referenced by a contact relationship.

The transformer does not retrieve missing entities and does not independently validate the finished dataset against an external SDS service.

For Power Automate transport, one flow reads one complete live V1 or V2.1 dataset and submits it to a Microsoft School Data Sync V2 data-flow ID configured for that same CSV format. Never mix versions, never read `.staging`, and provide the same complete file set used for the data flow's initial SDS upload each time. The word “V2” in the connector name identifies the current SDS experience; it does not restrict that experience to the V2.1 CSV format.
