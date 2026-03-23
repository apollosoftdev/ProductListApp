using Microsoft.Data.Sqlite;
using ProductListApp.Models;

namespace ProductListApp.Data;

public class DatabaseHelper
{
    private readonly string _connectionString;

    public DatabaseHelper()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProductListApp", "products.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Products (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Name        TEXT    NOT NULL,
                Category    TEXT    NOT NULL,
                Price       REAL    NOT NULL,
                Stock       INTEGER NOT NULL,
                Description TEXT    NOT NULL DEFAULT ''
            )
            """;
        cmd.ExecuteNonQuery();

        // Migration: add ImageData column if missing
        try
        {
            cmd.CommandText = "ALTER TABLE Products ADD COLUMN ImageData BLOB DEFAULT NULL";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException) { /* column already exists */ }

        cmd.CommandText = "SELECT COUNT(*) FROM Products";
        if ((long)cmd.ExecuteScalar()! == 0) SeedData(conn);
    }

    private static void SeedData(SqliteConnection conn)
    {
        var products = new (string Name, string Cat, double Price, int Stock, string Desc)[]
        {
            ("Wireless Mouse",        "Electronics",  29.99,  150, "Ergonomic wireless mouse with USB-C receiver"),
            ("Mechanical Keyboard",   "Electronics",  89.99,   75, "RGB backlit mechanical keyboard, hot-swappable switches"),
            ("USB-C Hub 7-in-1",      "Accessories",  45.50,  200, "HDMI, SD card reader, USB 3.0, PD charging"),
            ("Aluminum Monitor Stand", "Furniture",    34.99,  120, "Adjustable height with cable management"),
            ("Webcam 4K HDR",         "Electronics",  79.99,   90, "4K HDR webcam with AI noise cancellation mic"),
            ("LED Desk Lamp Pro",     "Furniture",    42.50,  180, "Dimmable LED with wireless charging base"),
            ("Laptop Sleeve 15\"",    "Accessories",  19.99,  300, "Water-resistant neoprene with magnetic closure"),
            ("Bluetooth Speaker",     "Electronics",  55.00,   60, "Portable Bluetooth 5.3, 18h battery, IP67"),
            ("Premium Notebook A5",   "Stationery",    8.99,  500, "Hardcover dotted grid, 200gsm pages"),
            ("Ergonomic Office Chair","Furniture",   299.00,   15, "Mesh back with adjustable lumbar and headrest"),
            ("HDMI 2.1 Cable 2m",     "Accessories",  14.99,  400, "8K@60Hz / 4K@120Hz, braided nylon"),
            ("Wireless Charger Pad",  "Electronics",  24.50,  160, "Qi2 compatible, 15W magnetic fast charge"),
            ("Noise Cancelling Buds", "Electronics", 129.99,   45, "ANC earbuds with spatial audio, 30h battery"),
            ("Standing Desk Mat",     "Furniture",    49.99,   85, "Anti-fatigue ergonomic mat, beveled edges"),
            ("Pen Set — Gel Ink",     "Stationery",   12.99,  250, "Set of 8 gel ink pens, 0.5mm fine tip"),
        };

        using var txn = conn.BeginTransaction();
        foreach (var p in products)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Products (Name, Category, Price, Stock, Description)
                VALUES ($name, $cat, $price, $stock, $desc)
                """;
            cmd.Parameters.AddWithValue("$name",  p.Name);
            cmd.Parameters.AddWithValue("$cat",   p.Cat);
            cmd.Parameters.AddWithValue("$price", p.Price);
            cmd.Parameters.AddWithValue("$stock", p.Stock);
            cmd.Parameters.AddWithValue("$desc",  p.Desc);
            cmd.ExecuteNonQuery();
        }
        txn.Commit();
    }

    public List<Product> GetAllProducts()
    {
        var list = new List<Product>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Category, Price, Stock, Description, ImageData FROM Products ORDER BY Id";

        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            list.Add(new Product
            {
                Id          = rdr.GetInt32(0),
                Name        = rdr.GetString(1),
                Category    = rdr.GetString(2),
                Price       = (decimal)rdr.GetDouble(3),
                Stock       = rdr.GetInt32(4),
                Description = rdr.GetString(5),
                ImageData   = rdr.IsDBNull(6) ? null : (byte[])rdr[6],
            });
        }
        return list;
    }

    public void AddProduct(Product p)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Products (Name, Category, Price, Stock, Description, ImageData)
            VALUES ($name, $cat, $price, $stock, $desc, $imageData)
            """;
        cmd.Parameters.AddWithValue("$name",  p.Name);
        cmd.Parameters.AddWithValue("$cat",   p.Category);
        cmd.Parameters.AddWithValue("$price", (double)p.Price);
        cmd.Parameters.AddWithValue("$stock", p.Stock);
        cmd.Parameters.AddWithValue("$desc",  p.Description);
        cmd.Parameters.AddWithValue("$imageData", (object?)p.ImageData ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void UpdateProduct(Product p)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Products SET Name=$name, Category=$cat, Price=$price,
                                Stock=$stock, Description=$desc, ImageData=$imageData
            WHERE Id=$id
            """;
        cmd.Parameters.AddWithValue("$id",    p.Id);
        cmd.Parameters.AddWithValue("$name",  p.Name);
        cmd.Parameters.AddWithValue("$cat",   p.Category);
        cmd.Parameters.AddWithValue("$price", (double)p.Price);
        cmd.Parameters.AddWithValue("$stock", p.Stock);
        cmd.Parameters.AddWithValue("$desc",  p.Description);
        cmd.Parameters.AddWithValue("$imageData", (object?)p.ImageData ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void DeleteProduct(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Products WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
