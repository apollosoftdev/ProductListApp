# Product Manager — WinUI 3 + SQLite

A modern Windows desktop app built with **WinUI 3** (Windows App SDK 1.8) + **.NET 9** + **SQLite**.

## Features

- **Mica backdrop** with fallback to Desktop Acrylic
- **Custom title bar** extending content into the title bar
- **Dashboard stats** — total products, categories, inventory value, low stock alerts
- **Product table** with ID, Name, Category badge, Price, Stock status, Description
- **Search** products in real-time (name, category, description)
- **Category filter** dropdown
- **Add product** dialog with editable category combo box
- **Delete** with confirmation dialog
- **Empty state** visual when no results match
- SQLite database with 15 auto-seeded sample products

## Prerequisites

1. **Windows 10** (v1809+) or **Windows 11**
2. **.NET 9 SDK** — [Download here](https://dotnet.microsoft.com/download/dotnet/9.0)

   > You currently have .NET 6 — you need to install .NET 9.
   > Both can coexist side by side on the same machine.
   >
   > **Fastest way to install:**
   > ```powershell
   > winget install Microsoft.DotNet.SDK.9
   > ```
   > Or download the installer from the link above.

3. After installing, verify:
   ```powershell
   dotnet --version
   # Should show 9.0.x
   ```

## How to Run

```bash
cd ProductListApp

# Restore NuGet packages
dotnet restore

# Build and run (defaults to x64)
dotnet run
```

Or open in **Visual Studio 2022** (v17.12+) and press **F5**.

## Project Structure

```
ProductListApp/
├── App.xaml / App.xaml.cs              # Application entry point
├── MainWindow.xaml / .xaml.cs          # Main UI — table, stats, dialogs, Mica backdrop
├── Models/
│   └── Product.cs                      # Product model with INotifyPropertyChanged
├── Data/
│   └── DatabaseHelper.cs              # SQLite init, seed, CRUD operations
├── ProductListApp.csproj              # .NET 9 + WinAppSDK 1.8 + SQLite
├── app.manifest                       # DPI awareness
└── README.md
```

## Tech Stack

| Component          | Version                   |
| ------------------ | ------------------------- |
| .NET               | 9.0                       |
| Windows App SDK    | 1.8.260317003 (latest)    |
| WinUI 3            | Included in WinAppSDK 1.8 |
| SQLite             | Microsoft.Data.Sqlite 9.x |
| Target Platform    | Windows 10.0.22621.0      |
| Min Platform       | Windows 10.0.17763.0      |

## Database

SQLite DB auto-created at:
```
%LOCALAPPDATA%\ProductListApp\products.db
```
Delete the file to reset to seed data.
