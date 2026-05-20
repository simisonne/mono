using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace mono.Core;

public sealed class LibraryDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public LibraryDb()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = System.IO.Path.Combine(appData, "mono");
        System.IO.Directory.CreateDirectory(dir);
        string dbPath = System.IO.Path.Combine(dir, "library.db");

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        _connection.Execute("""
            CREATE TABLE IF NOT EXISTS tracks (
                path TEXT PRIMARY KEY,
                playCount INTEGER DEFAULT 0,
                lastPlayed TEXT
            )
            """);
        _connection.Execute("""
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            )
            """);
        MigrateSchema(_connection);
    }

    private void MigrateSchema(IDbConnection db)
    {
        var cols = db.Query<string>(
            "SELECT name FROM pragma_table_info('tracks')").ToList();
        if (!cols.Contains("fingerprint"))
            db.Execute("ALTER TABLE tracks ADD COLUMN fingerprint TEXT");
        if (!cols.Contains("bpm"))
            db.Execute("ALTER TABLE tracks ADD COLUMN bpm REAL DEFAULT 0");
        if (!cols.Contains("musicalKey"))
            db.Execute("ALTER TABLE tracks ADD COLUMN musicalKey TEXT DEFAULT ''");
        if (!cols.Contains("lufs"))
            db.Execute("ALTER TABLE tracks ADD COLUMN lufs REAL DEFAULT 0");
        if (!cols.Contains("analysisVersion"))
            db.Execute("ALTER TABLE tracks ADD COLUMN analysisVersion INTEGER DEFAULT 0");
    }

    public TrackAnalysis? GetAnalysis(string path, string? fingerprint)
    {
        var result = _connection.QueryFirstOrDefault<TrackAnalysis>(
            "SELECT bpm, musicalKey, lufs, fingerprint FROM tracks WHERE path=@path",
            new { path });
        if (result != null && result.Bpm > 0) return result;

        if (fingerprint != null)
        {
            result = _connection.QueryFirstOrDefault<TrackAnalysis>(
                "SELECT bpm, musicalKey, lufs, fingerprint FROM tracks " +
                "WHERE fingerprint=@fp AND bpm > 0",
                new { fp = fingerprint });
            if (result != null)
            {
                _connection.Execute(
                    "DELETE FROM tracks WHERE path=@path",
                    new { path });
                _connection.Execute(
                    "UPDATE tracks SET path=@path WHERE fingerprint=@fp",
                    new { path, fp = fingerprint });
            }
        }
        return result;
    }

    public void SaveAnalysis(string path, string? fingerprint,
        double bpm, string key, double lufs)
    {
        _connection.Execute(
            "INSERT INTO tracks(path, fingerprint, bpm, musicalKey, lufs, " +
            "playCount, analysisVersion) " +
            "VALUES(@path,@fp,@bpm,@key,@lufs,0,1) " +
            "ON CONFLICT(path) DO UPDATE SET " +
            "fingerprint=@fp, bpm=@bpm, musicalKey=@key, " +
            "lufs=@lufs, analysisVersion=1",
            new { path, fp = fingerprint, bpm, key, lufs });
    }

    public void IncrementPlayCount(string path)
    {
        _connection.Execute("""
            INSERT INTO tracks (path, playCount, lastPlayed)
            VALUES (@path, 1, @now)
            ON CONFLICT(path) DO UPDATE SET
                playCount = playCount + 1,
                lastPlayed = @now
            """, new { path, now = DateTime.UtcNow.ToString("O") });
    }

    public int GetPlayCount(string path)
    {
        return _connection.QueryFirstOrDefault<int>(
            "SELECT playCount FROM tracks WHERE path = @path",
            new { path });
    }

    public void SaveSetting(string key, string value)
    {
        _connection.Execute(
            "INSERT INTO settings(key,value) VALUES(@key,@value) " +
            "ON CONFLICT(key) DO UPDATE SET value=@value",
            new { key, value });
    }

    public string? GetSetting(string key)
    {
        return _connection.QueryFirstOrDefault<string>(
            "SELECT value FROM settings WHERE key=@key", new { key });
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}

public class TrackAnalysis
{
    public double Bpm { get; set; }
    public string MusicalKey { get; set; } = "";
    public double Lufs { get; set; }
    public string? Fingerprint { get; set; }
}
