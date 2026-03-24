using Microsoft.Data.Sqlite;
using LedgerDesk.Models;

namespace LedgerDesk.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LedgerDesk", "ledgerdesk.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = Open();

        Execute(conn, "PRAGMA journal_mode=WAL;");
        Execute(conn, "PRAGMA foreign_keys=ON;");

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS Categories (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                Name      TEXT    NOT NULL UNIQUE,
                SortOrder INTEGER NOT NULL DEFAULT 0
            )
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS Records (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Title       TEXT    NOT NULL,
                Category    TEXT    NOT NULL,
                Description TEXT    NOT NULL DEFAULT '',
                Amount      REAL    NOT NULL,
                PaymentType INTEGER NOT NULL DEFAULT 0,
                Date        TEXT    NOT NULL,
                CreatedAt   TEXT    NOT NULL,
                UpdatedAt   TEXT    NOT NULL
            )
            """);

        // Migration: add PaymentType column if missing
        try
        {
            Execute(conn, "ALTER TABLE Records ADD COLUMN PaymentType INTEGER NOT NULL DEFAULT 0");
        }
        catch (SqliteException) { /* column already exists */ }

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS RecordImages (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                RecordId  INTEGER NOT NULL REFERENCES Records(Id) ON DELETE CASCADE,
                ImageData BLOB    NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            )
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            )
            """);

        // Indexes for filter performance
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_records_date ON Records(Date)");
        Execute(conn, "CREATE INDEX IF NOT EXISTS idx_records_category ON Records(Category)");

        // Seed default categories if empty
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Categories";
        if ((long)cmd.ExecuteScalar()! == 0)
        {
            SeedCategories(conn);
        }
    }

    private static void SeedCategories(SqliteConnection conn)
    {
        var categories = new[] { "Salary", "Food", "Transport", "Shopping", "Bills", "Entertainment", "Health", "Education", "Other" };
        using var txn = conn.BeginTransaction();
        for (int i = 0; i < categories.Length; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Categories (Name, SortOrder) VALUES ($name, $order)";
            cmd.Parameters.AddWithValue("$name", categories[i]);
            cmd.Parameters.AddWithValue("$order", i);
            cmd.ExecuteNonQuery();
        }
        txn.Commit();
    }

    // --- Records CRUD ---

    public List<Record> GetAllRecords()
    {
        using var conn = Open();
        return QueryRecords(conn, "SELECT Id, Title, Category, Description, Amount, PaymentType, Date, CreatedAt, UpdatedAt FROM Records ORDER BY Date DESC, Id DESC");
    }

    public List<Record> GetRecordsPaged(int page, int pageSize)
    {
        using var conn = Open();
        var offset = page * pageSize;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT Id, Title, Category, Description, Amount, PaymentType, Date, CreatedAt, UpdatedAt FROM Records ORDER BY Date DESC, Id DESC LIMIT {pageSize} OFFSET {offset}";

        var list = new List<Record>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var record = ReadRecord(rdr);
            LoadFirstImage(conn, record);
            list.Add(record);
        }
        return list;
    }

    public int GetRecordCount()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Records";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public Record? GetRecordById(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Category, Description, Amount, PaymentType, Date, CreatedAt, UpdatedAt FROM Records WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);

        using var rdr = cmd.ExecuteReader();
        return rdr.Read() ? ReadRecord(rdr) : null;
    }

    public List<Record> SearchRecords(RecordFilter filter, int page = -1, int pageSize = 50)
    {
        using var conn = Open();
        var (where, orderBy, parameters) = BuildFilterClause(filter);

        var sql = $"SELECT Id, Title, Category, Description, Amount, PaymentType, Date, CreatedAt, UpdatedAt FROM Records{where}{orderBy}";
        if (page >= 0)
            sql += $" LIMIT {pageSize} OFFSET {page * pageSize}";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        var list = new List<Record>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var record = ReadRecord(rdr);
            LoadFirstImage(conn, record);
            list.Add(record);
        }
        return list;
    }

    public int SearchRecordCount(RecordFilter filter)
    {
        using var conn = Open();
        var (where, _, parameters) = BuildFilterClause(filter);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM Records{where}";
        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static (string where, string orderBy, List<SqliteParameter> parameters) BuildFilterClause(RecordFilter filter)
    {
        var conditions = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (!string.IsNullOrWhiteSpace(filter.TitleQuery))
        {
            conditions.Add("Title LIKE $title");
            parameters.Add(new SqliteParameter("$title", $"%{filter.TitleQuery}%"));
        }
        if (!string.IsNullOrWhiteSpace(filter.CategoryFilter))
        {
            conditions.Add("Category = $category");
            parameters.Add(new SqliteParameter("$category", filter.CategoryFilter));
        }
        if (filter.PaymentTypeFilter.HasValue)
        {
            conditions.Add("PaymentType = $paymentType");
            parameters.Add(new SqliteParameter("$paymentType", filter.PaymentTypeFilter.Value));
        }
        if (!string.IsNullOrWhiteSpace(filter.DescriptionQuery))
        {
            conditions.Add("Description LIKE $desc");
            parameters.Add(new SqliteParameter("$desc", $"%{filter.DescriptionQuery}%"));
        }
        if (filter.AmountMin.HasValue)
        {
            conditions.Add("Amount >= $amountMin");
            parameters.Add(new SqliteParameter("$amountMin", (double)filter.AmountMin.Value));
        }
        if (filter.AmountMax.HasValue)
        {
            conditions.Add("Amount <= $amountMax");
            parameters.Add(new SqliteParameter("$amountMax", (double)filter.AmountMax.Value));
        }
        if (filter.DateStart.HasValue)
        {
            conditions.Add("Date >= $dateStart");
            parameters.Add(new SqliteParameter("$dateStart", filter.DateStart.Value.ToString("yyyy-MM-dd")));
        }
        if (filter.DateEnd.HasValue)
        {
            conditions.Add("Date <= $dateEnd");
            parameters.Add(new SqliteParameter("$dateEnd", filter.DateEnd.Value.ToString("yyyy-MM-dd")));
        }

        var where = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";

        var sortCol = filter.SortBy switch
        {
            "title" => "Title",
            "amount" => "Amount",
            "category" => "Category",
            "type" => "PaymentType",
            _ => "Date",
        };
        var dir = filter.SortDescending ? "DESC" : "ASC";
        var orderBy = $" ORDER BY {sortCol} {dir}, Id DESC";

        return (where, orderBy, parameters);
    }

    public int AddRecord(Record record)
    {
        using var conn = Open();
        var now = DateTime.Now.ToString("o");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Records (Title, Category, Description, Amount, PaymentType, Date, CreatedAt, UpdatedAt)
            VALUES ($title, $category, $desc, $amount, $balanceType, $date, $createdAt, $updatedAt);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$title", record.Title);
        cmd.Parameters.AddWithValue("$category", record.Category);
        cmd.Parameters.AddWithValue("$desc", record.Description);
        cmd.Parameters.AddWithValue("$amount", (double)record.Amount);
        cmd.Parameters.AddWithValue("$balanceType", record.PaymentType);
        cmd.Parameters.AddWithValue("$date", record.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$createdAt", now);
        cmd.Parameters.AddWithValue("$updatedAt", now);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateRecord(Record record)
    {
        using var conn = Open();
        var now = DateTime.Now.ToString("o");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Records SET Title=$title, Category=$category, Description=$desc,
                               Amount=$amount, PaymentType=$balanceType, Date=$date, UpdatedAt=$updatedAt
            WHERE Id=$id
            """;
        cmd.Parameters.AddWithValue("$id", record.Id);
        cmd.Parameters.AddWithValue("$title", record.Title);
        cmd.Parameters.AddWithValue("$category", record.Category);
        cmd.Parameters.AddWithValue("$desc", record.Description);
        cmd.Parameters.AddWithValue("$amount", (double)record.Amount);
        cmd.Parameters.AddWithValue("$balanceType", record.PaymentType);
        cmd.Parameters.AddWithValue("$date", record.Date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$updatedAt", now);
        cmd.ExecuteNonQuery();
    }

    public void DeleteRecord(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Records WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // --- Record Images ---

    public List<RecordImage> GetImagesForRecord(int recordId)
    {
        using var conn = Open();
        var list = new List<RecordImage>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, RecordId, ImageData, SortOrder FROM RecordImages WHERE RecordId = $recordId ORDER BY SortOrder";
        cmd.Parameters.AddWithValue("$recordId", recordId);

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            list.Add(new RecordImage
            {
                Id = rdr.GetInt32(0),
                RecordId = rdr.GetInt32(1),
                ImageData = (byte[])rdr[2],
                SortOrder = rdr.GetInt32(3),
            });
        }
        return list;
    }

    public void AddImage(int recordId, byte[] imageData, int sortOrder)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO RecordImages (RecordId, ImageData, SortOrder) VALUES ($recordId, $imageData, $sortOrder)";
        cmd.Parameters.AddWithValue("$recordId", recordId);
        cmd.Parameters.AddWithValue("$imageData", imageData);
        cmd.Parameters.AddWithValue("$sortOrder", sortOrder);
        cmd.ExecuteNonQuery();
    }

    public void DeleteImage(int imageId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RecordImages WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", imageId);
        cmd.ExecuteNonQuery();
    }

    public void DeleteImagesForRecord(int recordId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RecordImages WHERE RecordId = $recordId";
        cmd.Parameters.AddWithValue("$recordId", recordId);
        cmd.ExecuteNonQuery();
    }

    // --- Categories ---

    public List<Category> GetAllCategories()
    {
        using var conn = Open();
        var list = new List<Category>();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, SortOrder FROM Categories ORDER BY SortOrder";

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            list.Add(new Category
            {
                Id = rdr.GetInt32(0),
                Name = rdr.GetString(1),
                SortOrder = rdr.GetInt32(2),
            });
        }
        return list;
    }

    public void AddCategory(string name, int sortOrder)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Categories (Name, SortOrder) VALUES ($name, $order)";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$order", sortOrder);
        cmd.ExecuteNonQuery();
    }

    public void RenameCategory(int id, string newName)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Categories SET Name = $name WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", newName);
        cmd.ExecuteNonQuery();
    }

    public void DeleteCategory(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Categories WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void ReassignCategory(string oldCategory, string newCategory)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Records SET Category = $newCat WHERE Category = $oldCat";
        cmd.Parameters.AddWithValue("$oldCat", oldCategory);
        cmd.Parameters.AddWithValue("$newCat", newCategory);
        cmd.ExecuteNonQuery();
    }

    // --- AppSettings ---

    public string? GetSetting(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppSettings (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = $value
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteSetting(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM AppSettings WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.ExecuteNonQuery();
    }

    // --- Data Management ---

    public void ClearAllRecords()
    {
        using var conn = Open();
        Execute(conn, "DELETE FROM RecordImages");
        Execute(conn, "DELETE FROM Records");
    }

    public void ResetDatabase()
    {
        using var conn = Open();
        Execute(conn, "DELETE FROM RecordImages");
        Execute(conn, "DELETE FROM Records");
        Execute(conn, "DELETE FROM Categories");
        Execute(conn, "DELETE FROM AppSettings");
        SeedCategories(conn);
    }

    // --- Test Data ---

    public void SeedTestData(int count = 2000)
    {
        using var conn = Open();

        // Fix existing records: negative amount should be PaymentType=1
        Execute(conn, "UPDATE Records SET PaymentType = 1 WHERE Amount < 0 AND PaymentType = 0");
        Execute(conn, "UPDATE Records SET PaymentType = 0 WHERE Amount > 0 AND PaymentType = 1");

        // Check if already seeded
        using var chk = conn.CreateCommand();
        chk.CommandText = "SELECT COUNT(*) FROM Records";
        if (Convert.ToInt32(chk.ExecuteScalar()) >= count) return;

        var categories = new[] { "Salary", "Food", "Transport", "Shopping", "Bills", "Entertainment", "Health", "Education", "Other" };
        var titles = new[]
        {
            "Monthly Salary", "Freelance Payment", "Grocery Store", "Restaurant Dinner",
            "Bus Fare", "Taxi Ride", "Online Shopping", "Electronics Store",
            "Electricity Bill", "Water Bill", "Internet Bill", "Phone Bill",
            "Movie Tickets", "Concert", "Gym Membership", "Doctor Visit",
            "Pharmacy", "Online Course", "Book Purchase", "Coffee Shop",
            "Gas Station", "Parking Fee", "Clothing Store", "Gift Purchase",
            "Insurance Premium", "Rent Payment", "Subscription Service", "Donation",
            "Bonus", "Investment Return", "Refund", "Side Project Income",
        };

        var rng = new Random(42);
        using var txn = conn.BeginTransaction();
        var now = DateTime.Now;

        for (int i = 0; i < count; i++)
        {
            var isIncome = rng.Next(100) < 25; // 25% income
            var cat = categories[rng.Next(categories.Length)];
            var title = titles[rng.Next(titles.Length)];
            var amount = isIncome
                ? Math.Round((decimal)(rng.NextDouble() * 5000 + 500), 2)
                : -Math.Round((decimal)(rng.NextDouble() * 200 + 5), 2);
            var date = now.AddDays(-rng.Next(365 * 2)).ToString("yyyy-MM-dd");
            var ts = now.ToString("o");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Records (Title, Category, Description, Amount, PaymentType, Date, CreatedAt, UpdatedAt)
                VALUES ($title, $cat, $desc, $amount, $balanceType, $date, $ts, $ts)
                """;
            cmd.Parameters.AddWithValue("$title", title);
            cmd.Parameters.AddWithValue("$cat", cat);
            cmd.Parameters.AddWithValue("$desc", $"Test record #{i + 1}");
            cmd.Parameters.AddWithValue("$amount", (double)amount);
            cmd.Parameters.AddWithValue("$balanceType", isIncome ? 0 : 1);
            cmd.Parameters.AddWithValue("$date", date);
            cmd.Parameters.AddWithValue("$ts", ts);
            cmd.ExecuteNonQuery();
        }
        txn.Commit();
    }

    // --- Helpers ---

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static Record ReadRecord(SqliteDataReader rdr)
    {
        return new Record
        {
            Id = rdr.GetInt32(0),
            Title = rdr.GetString(1),
            Category = rdr.GetString(2),
            Description = rdr.GetString(3),
            Amount = (decimal)rdr.GetDouble(4),
            PaymentType = rdr.GetInt32(5),
            Date = DateTime.Parse(rdr.GetString(6)),
            CreatedAt = DateTime.Parse(rdr.GetString(7)),
            UpdatedAt = DateTime.Parse(rdr.GetString(8)),
        };
    }

    private static List<Record> QueryRecords(SqliteConnection conn, string sql)
    {
        var list = new List<Record>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var record = ReadRecord(rdr);
            LoadFirstImage(conn, record);
            list.Add(record);
        }
        return list;
    }

    private static void LoadFirstImage(SqliteConnection conn, Record record)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ImageData FROM RecordImages WHERE RecordId = $id ORDER BY SortOrder LIMIT 1";
        cmd.Parameters.AddWithValue("$id", record.Id);
        var result = cmd.ExecuteScalar();
        if (result is byte[] data)
        {
            record.HasImages = true;
            record.FirstImageData = data;
        }
    }
}
