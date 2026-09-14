using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MyMelody.Core;

internal sealed class PracticeStore : IDisposable
{
    // Increment only alongside a transactional migration and a pre-migration backup.
    internal const int SchemaVersion = 1;
    private const int ApplicationId = 0x4D59504D;
    private readonly SqliteConnection _connection;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public string DatabasePath { get; }

    public PracticeStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        DatabasePath = Path.Combine(dataDirectory, "practice.sqlite");
        bool isNew = !File.Exists(DatabasePath);
        _connection = Open(DatabasePath, false);
        try
        {
            if (isNew) Initialize();
            CheckDatabase(_connection);
            Execute(_connection, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;");
        }
        catch { _connection.Dispose(); throw; }
    }

    private static SqliteConnection Open(string path, bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path), Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false, DefaultTimeout = 5
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private void Initialize()
    {
        using var transaction = _connection.BeginTransaction();
        Execute(_connection, $$"""
            PRAGMA application_id={{ApplicationId}};
            PRAGMA user_version={{SchemaVersion}};
            CREATE TABLE app_state(id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
            CREATE TABLE practice_sessions(
                id TEXT PRIMARY KEY, start_utc TEXT NOT NULL, end_utc TEXT NOT NULL,
                local_date TEXT NOT NULL, seconds REAL NOT NULL CHECK(seconds>=0),
                note_count INTEGER NOT NULL CHECK(note_count>=0), mode INTEGER NOT NULL CHECK(mode IN (0,1)));
            CREATE INDEX idx_practice_date ON practice_sessions(local_date);
            INSERT INTO app_state(id,json) VALUES(1,'{}');
            """, transaction);
        transaction.Commit();
    }

    private static void CheckDatabase(SqliteConnection connection)
    {
        if (Convert.ToInt32(Scalar(connection, "PRAGMA application_id;"), CultureInfo.InvariantCulture) != ApplicationId)
            throw new InvalidDataException("마이멜로디 연습 친구의 저장 파일이 아닙니다.");
        int version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        if (version != SchemaVersion)
            throw new InvalidDataException($"지원하지 않는 저장 버전입니다 ({version}). 현재 데이터는 변경되지 않았습니다.");
        if (!string.Equals(Convert.ToString(Scalar(connection, "PRAGMA quick_check;"), CultureInfo.InvariantCulture), "ok", StringComparison.Ordinal))
            throw new InvalidDataException("저장 파일이 손상되었습니다. 백업을 복원해 주세요.");
    }

    public (AppState State, AppSettings Settings, List<PracticeSession> Sessions) Load() => Read(_connection);

    private static (AppState State, AppSettings Settings, List<PracticeSession> Sessions) Read(SqliteConnection connection)
    {
        var payload = Convert.ToString(Scalar(connection, "SELECT json FROM app_state WHERE id=1;"), CultureInfo.InvariantCulture)
            ?? throw new InvalidDataException("앱 상태가 없습니다.");
        var stored = JsonSerializer.Deserialize<StoredState>(payload, JsonOptions) ?? throw new InvalidDataException("앱 상태가 올바르지 않습니다.");
        var sessions = new List<PracticeSession>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,start_utc,end_utc,local_date,seconds,note_count,mode FROM practice_sessions ORDER BY start_utc,id;";
        using var reader = command.ExecuteReader();
        try
        {
            while (reader.Read()) sessions.Add(new PracticeSession
            {
                Id = reader.GetString(0), StartedAt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                EndedAt = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                LocalDate = DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                PracticeSeconds = reader.GetDouble(4), NoteCount = reader.GetInt64(5), Mode = (PracticeMode)reader.GetInt32(6)
            });
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            throw new InvalidDataException("저장된 연습 기록의 날짜 또는 숫자가 손상되었습니다.", ex);
        }
        Validate(stored.State, stored.Settings, sessions);
        stored.Settings.Normalize();
        return (stored.State, stored.Settings, sessions);
    }

    private static void Validate(AppState? state, AppSettings? settings, List<PracticeSession> sessions)
    {
        if (state?.Characters is null || settings is null || state.Characters.Count > CharacterCatalog.All.Count)
            throw new InvalidDataException("저장된 앱 상태가 올바르지 않습니다.");
        var known = CharacterCatalog.All.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var character in state.Characters)
        {
            if (character is null || !known.Contains(character.Id) || !ids.Add(character.Id) ||
                !double.IsFinite(character.PracticeSeconds) || character.PracticeSeconds < 0 || character.PracticeSeconds > GrowthRules.CompletionSeconds ||
                (character.IsComplete != character.CompletedAt.HasValue) ||
                character.AcquiredAt == default || character.CompletedAt < character.AcquiredAt)
                throw new InvalidDataException("저장된 캐릭터 정보가 올바르지 않습니다.");
        }
        if ((state.GrowingCharacterId is not null && !ids.Contains(state.GrowingCharacterId)) ||
            (state.DisplayCharacterId is not null && !ids.Contains(state.DisplayCharacterId)) ||
            state.Characters.Count(x => !x.IsComplete) > 1 ||
            state.Characters.Any(x => !x.IsComplete && x.Id != state.GrowingCharacterId) ||
            (state.Characters.Count > 0 && (state.DisplayCharacterId is null || state.GrowingCharacterId is null)))
            throw new InvalidDataException("육성 또는 표시 캐릭터 정보가 올바르지 않습니다.");
        if (state.DisplayStage is int displayStage &&
            (state.DisplayCharacter is not { } displayCharacter || displayStage < 1 || displayStage > displayCharacter.Stage))
            throw new InvalidDataException("저장된 표시 단계가 올바르지 않습니다.");
        var sessionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            if (!Guid.TryParseExact(session.Id, "N", out _) || !sessionIds.Add(session.Id) ||
                !double.IsFinite(session.PracticeSeconds) || session.PracticeSeconds < 0 || session.NoteCount < 0 ||
                !Enum.IsDefined(session.Mode) || session.StartedAt == default || session.EndedAt < session.StartedAt ||
                session.PracticeSeconds > (session.EndedAt - session.StartedAt).TotalSeconds + 0.01)
                throw new InvalidDataException("연습 기록이 올바르지 않습니다.");
        }
    }

    public void Save(AppState state, AppSettings settings, IReadOnlyList<PracticeSession> sessions, ISet<string> dirtySessions)
    {
        settings.Normalize();
        using var transaction = _connection.BeginTransaction();
        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE app_state SET json=$json WHERE id=1;";
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(new StoredState { State = state, Settings = settings }));
            command.ExecuteNonQuery();
        }
        using var upsert = _connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO practice_sessions(id,start_utc,end_utc,local_date,seconds,note_count,mode)
            VALUES($id,$start,$end,$date,$seconds,$notes,$mode)
            ON CONFLICT(id) DO UPDATE SET end_utc=excluded.end_utc,seconds=excluded.seconds,note_count=excluded.note_count;
            """;
        foreach (var session in sessions.Where(x => dirtySessions.Contains(x.Id)))
        {
            upsert.Parameters.Clear();
            upsert.Parameters.AddWithValue("$id", session.Id);
            upsert.Parameters.AddWithValue("$start", session.StartedAt.ToUniversalTime().ToString("O"));
            upsert.Parameters.AddWithValue("$end", session.EndedAt.ToUniversalTime().ToString("O"));
            upsert.Parameters.AddWithValue("$date", session.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            upsert.Parameters.AddWithValue("$seconds", session.PracticeSeconds);
            upsert.Parameters.AddWithValue("$notes", session.NoteCount);
            upsert.Parameters.AddWithValue("$mode", (int)session.Mode);
            upsert.ExecuteNonQuery();
        }
        transaction.Commit();
        dirtySessions.Clear();
    }

    public void Backup(string destination, DateTimeOffset now)
    {
        destination = Path.GetFullPath(destination);
        if (string.Equals(destination, DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("현재 저장 파일에는 백업을 덮어쓸 수 없습니다.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string tempDirectory = Path.Combine(Path.GetDirectoryName(DatabasePath)!, ".backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string tempZip = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string snapshotPath = Path.Combine(tempDirectory, "practice.sqlite");
            using (var snapshot = Open(snapshotPath, false)) _connection.BackupDatabase(snapshot);
            byte[] databaseBytes = File.ReadAllBytes(snapshotPath);
            using (var output = new FileStream(tempZip, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                using (var db = archive.CreateEntry("practice.sqlite", CompressionLevel.Optimal).Open()) db.Write(databaseBytes);
                using var manifest = archive.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(manifest, new BackupManifest("MyMelodyPractice", SchemaVersion, now, Convert.ToHexString(SHA256.HashData(databaseBytes))));
            }
            File.Move(tempZip, destination, true);
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
            // This directory is a literal child generated above, never derived from ZIP entry names.
            DeleteTemporaryDirectory(tempDirectory);
        }
    }

    public (AppState State, AppSettings Settings, List<PracticeSession> Sessions) Restore(string source, Action beforeReplace)
    {
        string tempDirectory = Path.Combine(Path.GetDirectoryName(DatabasePath)!, ".restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            using (var input = File.OpenRead(source))
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
            {
                if (archive.Entries.Count != 2) throw new InvalidDataException("백업 파일의 구성이 올바르지 않습니다.");
                var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("백업 설명이 없습니다.");
                var databaseEntry = archive.GetEntry("practice.sqlite") ?? throw new InvalidDataException("백업 데이터가 없습니다.");
                if (manifestEntry.Length > 65536 || databaseEntry.Length > 512L * 1024 * 1024)
                    throw new InvalidDataException("백업 파일 크기가 지원 범위를 초과했습니다.");
                BackupManifest manifest;
                using (var manifestStream = manifestEntry.Open())
                    manifest = JsonSerializer.Deserialize<BackupManifest>(manifestStream, JsonOptions) ?? throw new InvalidDataException("백업 설명이 손상되었습니다.");
                if (manifest.Application != "MyMelodyPractice" || manifest.SchemaVersion != SchemaVersion)
                    throw new InvalidDataException("지원하지 않는 앱 또는 백업 버전입니다.");
                using var databaseStream = databaseEntry.Open();
                using var memory = new MemoryStream();
                databaseStream.CopyTo(memory);
                byte[] bytes = memory.ToArray();
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("백업 파일의 무결성 확인에 실패했습니다.");
                File.WriteAllBytes(Path.Combine(tempDirectory, "practice.sqlite"), bytes);
            }
            using var candidate = Open(Path.Combine(tempDirectory, "practice.sqlite"), true);
            CheckDatabase(candidate);
            var validated = Read(candidate);
            beforeReplace(); // Current state is saved and backed up only after candidate passes every check.
            candidate.BackupDatabase(_connection); // SQLite commits the replacement atomically.
            return validated;
        }
        // Cleanup must not turn a committed replacement into an apparent failure:
        // the manager must always adopt the state now present in SQLite.
        finally { DeleteTemporaryDirectory(tempDirectory); }
    }

    internal static string RecoverFromBackup(string dataDirectory, string sourceZip)
    {
        dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        sourceZip = Path.GetFullPath(sourceZip);
        string parent = Path.GetDirectoryName(dataDirectory) ?? throw new ArgumentException("데이터 폴더는 드라이브 루트일 수 없습니다.", nameof(dataDirectory));
        Directory.CreateDirectory(parent);
        // A sibling guarantees same-volume rename/replace. No archive entry chooses a path.
        string stagingDirectory = Path.Combine(parent, ".mymelody-recovery-" + Guid.NewGuid().ToString("N"));
        string? recoveryDirectory = null;
        var heldSidecars = new List<(string Original, string Held)>();
        bool replaced = false;
        try
        {
            using (var staging = new PracticeStore(stagingDirectory)) staging.Restore(sourceZip, () => { });
            string candidatePath = Path.Combine(stagingDirectory, "practice.sqlite");
            // Closing the staging store checkpoints WAL. Reopen the resulting standalone
            // database to verify exactly the bytes that will replace the damaged file.
            using (var candidate = Open(candidatePath, true)) { CheckDatabase(candidate); _ = Read(candidate); }

            Directory.CreateDirectory(dataDirectory);
            recoveryDirectory = Path.Combine(dataDirectory, "Recovery", $"before-recovery-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(recoveryDirectory);
            string databasePath = Path.Combine(dataDirectory, "practice.sqlite");
            var originalPaths = new[] { databasePath, databasePath + "-wal", databasePath + "-shm" };
            // Preserve every original byte before moving a sidecar or replacing the DB.
            // FileShare.Read also refuses an ordinary concurrently open SQLite writer.
            foreach (string original in originalPaths.Where(File.Exists))
            {
                using var input = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(Path.Combine(recoveryDirectory, Path.GetFileName(original)), FileMode.CreateNew, FileAccess.Write);
                input.CopyTo(output); output.Flush(flushToDisk: true);
            }
            foreach (string sidecar in originalPaths.Skip(1).Where(File.Exists))
            {
                string held = Path.Combine(recoveryDirectory, Path.GetFileName(sidecar) + ".held");
                File.Move(sidecar, held);
                heldSidecars.Add((sidecar, held));
            }
            // SQLite must never see old WAL bytes alongside the new main database.
            if (File.Exists(databasePath)) File.Replace(candidatePath, databasePath, null);
            else File.Move(candidatePath, databasePath);
            replaced = true;
            return recoveryDirectory;
        }
        catch
        {
            if (!replaced)
                foreach (var sidecar in heldSidecars.AsEnumerable().Reverse())
                    if (File.Exists(sidecar.Held) && !File.Exists(sidecar.Original)) File.Move(sidecar.Held, sidecar.Original);
            throw;
        }
        finally
        {
            // After replacement, .held files are redundant with the preserved exact copies.
            // A failed cleanup is harmless; the original recovery files are never removed.
            if (replaced)
                foreach (var sidecar in heldSidecars)
                    try { File.Delete(sidecar.Held); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (Directory.Exists(stagingDirectory))
                try { Directory.Delete(stagingDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar();
    }
    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; command.ExecuteNonQuery();
    }
    private static void DeleteTemporaryDirectory(string path)
    {
        try { Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose() => _connection.Dispose();
    private sealed record BackupManifest(string Application, int SchemaVersion, DateTimeOffset CreatedAt, string Sha256);
}
