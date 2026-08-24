import sqlite3

from rhino_mcp.regulations import RegulationLibrary, RegulationTools, packaged_database_path


def make_database(path):
    connection = sqlite3.connect(path)
    connection.executescript(
        """
        CREATE TABLE metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        INSERT INTO metadata VALUES ('schema_version', '1');
        INSERT INTO metadata VALUES ('snapshot_created', '2026-08-24T00:00:00Z');
        INSERT INTO metadata VALUES ('source_folder_url', 'https://drive.google.com/example');
        CREATE TABLE documents (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            folder TEXT NOT NULL,
            category TEXT NOT NULL,
            drive_url TEXT NOT NULL,
            modified_time TEXT,
            indexed_pages INTEGER NOT NULL
        );
        CREATE TABLE pages (
            id INTEGER PRIMARY KEY,
            document_id TEXT NOT NULL,
            page_number INTEGER NOT NULL,
            text TEXT NOT NULL
        );
        CREATE VIRTUAL TABLE pages_fts USING fts5(title, text, content='');
        INSERT INTO documents VALUES (
            'fire-code', 'AzDTN 2.6-1 Fire safety code', 'AzDTN', 'regulation',
            'https://drive.google.com/fire-code', '2026-07-01', 1
        );
        INSERT INTO pages VALUES (
            1, 'fire-code', 12,
            'Yanğın zamanı təxliyə çıxışları bina daxilində maneəsiz qalmalıdır.'
        );
        INSERT INTO pages_fts(rowid, title, text) VALUES (
            1, 'AzDTN 2.6-1 Fire safety code',
            'Yanğın zamanı təxliyə çıxışları bina daxilində maneəsiz qalmalıdır.'
        );
        """
    )
    connection.close()


def test_status_search_and_page_are_citation_first(tmp_path):
    database = tmp_path / "regulations.sqlite3"
    make_database(database)
    library = RegulationLibrary(database=database)

    status = library.status()
    assert status["ok"] is True
    assert status["indexed_documents"] == 1
    assert status["indexed_pages"] == 1

    result = library.search("fire evacuation", limit=3)
    assert result["count"] == 1
    assert result["results"][0]["source_id"] == "fire-code"
    assert result["results"][0]["page"] == 12
    assert "təxliyə çıxışları" in result["results"][0]["excerpt"]
    assert "page 12" in result["results"][0]["citation"]

    code_result = library.search("AzDTN 2.6-1", limit=3)
    assert code_result["count"] == 1
    assert code_result["results"][0]["source_id"] == "fire-code"

    page = library.page("fire-code", 12)
    assert page["ok"] is True
    assert page["text"].startswith("Yanğın zamanı")
    assert page["drive_url"].endswith("fire-code")
    library.close()


def test_checklist_marks_missing_project_inputs_and_never_certifies(tmp_path):
    database = tmp_path / "regulations.sqlite3"
    make_database(database)
    library = RegulationLibrary(database=database)

    result = library.checklist(
        "A small public building with one main entrance",
        topics=["fire_and_life_safety"],
    )

    assert result["ok"] is True
    assert result["missing_inputs"] == ["jurisdiction", "building type / occupancy"]
    assert result["topics"][0]["verified_in_library"] is True
    assert "not a legal compliance certificate" in result["disclaimer"]


def test_missing_library_is_actionable(tmp_path):
    status = RegulationLibrary(database=tmp_path / "missing.sqlite3").status()
    assert status["ok"] is False
    assert "not installed" in status["message"]
    assert "RHINO_MCP_REGULATIONS_DB" in status["next_step"]


def test_regulation_tools_are_registered():
    class App:
        def __init__(self):
            self.registered = []

        def tool(self):
            def register(value):
                self.registered.append(value.__name__)
                return value

            return register

    app = App()
    RegulationTools(app, settings=None)
    assert app.registered == [
        "regulation_library_status",
        "search_regulations",
        "get_regulation_page",
        "architecture_regulation_checklist",
    ]


def test_packaged_regulation_snapshot_is_complete():
    database = packaged_database_path()
    assert database.is_file()
    assert database.stat().st_size < 100 * 1024 * 1024

    library = RegulationLibrary(database=database)
    status = library.status()
    assert status["documents"] == 289
    assert status["indexed_documents"] == 272
    assert status["indexed_pages"] == 7896
    assert status["source_folder"].endswith("13y5jvSC_KyE5Hm0N9fdVqXXnCXFp5FLz")
    library.close()
