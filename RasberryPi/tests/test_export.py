from raspberry_module.config import AppConfig
from raspberry_module.storage import Storage


def test_export_csv(tmp_path):
    config = AppConfig.load()
    storage = Storage(tmp_path / "test.db")

    exports = storage.export_csv(tmp_path)

    assert "command_log" in exports
    assert (tmp_path / "command_log.csv").exists()
