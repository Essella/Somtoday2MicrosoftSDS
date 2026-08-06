import argparse
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Genereer lege SDS CSV-bestanden voor v1/v2 met of zonder guardians."
        )
    )
    parser.add_argument(
        "--version",
        choices=("1", "2", "all"),
        help="CSV-versie (1, 2, of all). Als niet gezet, wordt gevraagd.",
    )
    parser.add_argument(
        "--guardians",
        choices=("j", "n", "with", "without", "both"),
        help=(
            "Guardian-keuze (j/n, with/without, of both). "
            "Als niet gezet, wordt gevraagd (of both bij --version all)."
        ),
    )
    parser.add_argument(
        "--output",
        default=str(Path.cwd()),
        help="Outputmap voor gegenereerde bestanden.",
    )
    return parser.parse_args()


def resolve_guardians_choice(value: str) -> str:
    normalized = value.strip().lower()
    if normalized in ("j", "with"):
        return "with"
    if normalized in ("n", "without"):
        return "without"
    if normalized == "both":
        return "both"

    raise ValueError(
        "Ongeldige guardians-keuze. Gebruik j/n, with/without, of both."
    )


def prompt_version() -> str:
    while True:
        version = input("Versie kiezen (1 of 2): ").strip().lower()
        if version in ("1", "2"):
            return version
        print("Ongeldige keuze. Kies 1 of 2.")


def prompt_guardians() -> str:
    while True:
        guardian_choice = input(
            "Inclusief ouders/verzorgers? J/N: "
        ).strip().upper()

        if guardian_choice in ("J", "N"):
            return resolve_guardians_choice(guardian_choice)

        print("Ongeldige keuze. Kies J of N.")


def get_files(version: str, with_guardians: bool) -> dict[str, str]:
    if version == "1":
        files = {
            "School.csv": "SIS ID,Name",
            "Section.csv": (
                "SIS ID,School SIS ID,Section Name,Section Number,"
                "Course Name,Course Description"
            ),
            "Student.csv": "SIS ID,School SIS ID,Username",
            "StudentEnrollment.csv": "Section SIS ID,SIS ID",
            "Teacher.csv": "SIS ID,School SIS ID,Username",
            "TeacherRoster.csv": "Section SIS ID,SIS ID",
        }

        if with_guardians:
            files["Guardianrelationship.csv"] = "SIS ID,Email,Role"
            files["User.csv"] = "Email,First Name,Last Name,Phone,SIS ID"

        return files

    files = {
        "classes.csv": (
            "sourcedId,orgSourcedId,title,sessionSourcedIds,"
            "courseSourcedId"
        ),
        "enrollments.csv": "classSourcedId,userSourcedId,role",
        "orgs.csv": "sourcedId,name,type,parentSourcedId",
        "roles.csv": "userSourcedId,orgSourcedId,role",
        "users.csv": (
            "sourcedId,username,givenName,familyName,password,"
            "activeDirectoryMatchId,email,phone,sms"
        ),
    }

    if with_guardians:
        files["relationships.csv"] = (
            "userSourcedId,relationshipUserSourcedId,relationshipRole"
        )

    return files


def set_name(version: str, with_guardians: bool) -> str:
    guardian_suffix = "with-guardians" if with_guardians else "no-guardians"
    return f"v{version}-{guardian_suffix}"


def build_combinations(version: str, guardians_choice: str) -> list[tuple[str, bool]]:
    versions = ["1", "2"] if version == "all" else [version]
    combinations: list[tuple[str, bool]] = []

    for current_version in versions:
        if guardians_choice == "with":
            combinations.append((current_version, True))
        elif guardians_choice == "without":
            combinations.append((current_version, False))
        else:
            combinations.append((current_version, False))
            combinations.append((current_version, True))

    return combinations


def write_set(target_folder: Path, files: dict[str, str]) -> None:
    target_folder.mkdir(parents=True, exist_ok=True)
    for filename, header in files.items():
        file_path = target_folder / filename
        file_path.write_text(header + "\n", encoding="utf-8")
        print(f"Aangemaakt: {file_path}")


def main() -> None:
    args = parse_args()

    version = args.version if args.version else prompt_version()

    if args.guardians:
        guardians_choice = resolve_guardians_choice(args.guardians)
    elif version == "all":
        guardians_choice = "both"
    else:
        guardians_choice = prompt_guardians()

    combinations = build_combinations(version, guardians_choice)

    output_root = Path(args.output).resolve()
    output_root.mkdir(parents=True, exist_ok=True)

    single_set_to_base_folder = len(combinations) == 1
    for current_version, with_guardians in combinations:
        files = get_files(current_version, with_guardians)
        if single_set_to_base_folder:
            target_folder = output_root
        else:
            target_folder = output_root / set_name(current_version, with_guardians)

        write_set(target_folder, files)


if __name__ == "__main__":
    main()