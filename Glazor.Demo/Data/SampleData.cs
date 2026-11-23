using Glazor.Grds.Mdls;

namespace Glazor.Demo.Data;

public enum ProductCategory { Tools, Toys, Food, Books, Garden }

public record Product(int Id, string Code, string Name, ProductCategory Category, decimal Price, int? Stock, DateTime Updated, bool Active, string? Notes,
    string Supplier, string Sku, double Weight, string Color, string Size, int Rating, DateOnly Created, bool Discontinued, string Warehouse, string Barcode);

/// <summary>A row of the tree page: the record is the node, as the docs recommend, with the level and the expansion stamped on it.</summary>
public sealed record PlaceNode(int Id, string Name, int Level, int? ParentId, int Population, double Area, string Capital, string Currency, string Timezone, int Founded, string Code) : ITreeNode
{
    public bool Expanded { get; set; } = true;
    public List<PlaceNode> Children { get; } = [];

    bool ITreeNode.IsExpanded() => Expanded;
    int ITreeNode.GetLevel() => Level;
    bool ITreeNode.IsLeaf() => Children.Count == 0;
    bool ITreeNode.IsSubTotal() => false;
    public bool HasChildren() => Children.Count > 0;

    public double Density => Area > 0 ? Math.Round(Population / Area, 1) : 0;
}

/// <summary>Deterministic sample rows, the same on every run.</summary>
public static class SampleData
{
    private static readonly string[] Adjectives = ["Red", "Blue", "Small", "Large", "Classic", "Modern", "Wooden", "Steel", "Soft", "Bright"];
    private static readonly string[] Things = ["Hammer", "Puzzle", "Bread", "Novel", "Rake", "Lamp", "Chair", "Kite", "Cheese", "Atlas"];
    private static readonly string[] Suppliers = ["Acme", "Globex", "Initech", "Umbrella", "Stark", "Wayne"];
    private static readonly string[] Colors = ["red", "blue", "green", "black", "white", "yellow"];
    private static readonly string[] Sizes = ["S", "M", "L", "XL"];
    private static readonly string[] Warehouses = ["North", "South", "Central"];
    private static readonly string[] Currencies = ["EUR", "USD", "GBP", "JPY", "CHF"];
    private static readonly string?[] Notes = [null, "", "Back-ordered until next month", "Ships in two parcels, the second one a week after the first, so the customer is told twice", "Display item", null];

    public static List<Product> Products(int count)
    {
        Random rnd = new(42);
        DateTime start = new(2025, 1, 1);
        List<Product> rows = new(count);
        for (int i = 1; i <= count; i++)
        {
            rows.Add(new Product(i, $"P{i:D4}", $"{Adjectives[rnd.Next(Adjectives.Length)]} {Things[rnd.Next(Things.Length)]} {i}",
                (ProductCategory)rnd.Next(5), Math.Round((decimal)rnd.NextDouble() * 200 + 1, 2), rnd.Next(4) == 0 ? null : rnd.Next(0, 500),
                start.AddDays(rnd.Next(0, 600)).AddMinutes(rnd.Next(0, 1440)), rnd.Next(5) != 0, Notes[rnd.Next(Notes.Length)],
                Suppliers[rnd.Next(Suppliers.Length)], $"SKU-{i:D5}-{rnd.Next(100):D2}", Math.Round(rnd.NextDouble() * 20, 2), Colors[rnd.Next(Colors.Length)],
                Sizes[rnd.Next(Sizes.Length)], rnd.Next(1, 6), DateOnly.FromDateTime(start.AddDays(-rnd.Next(0, 2000))), rnd.Next(10) == 0,
                Warehouses[rnd.Next(Warehouses.Length)], $"{rnd.NextInt64(1_000_000_000_000, 9_999_999_999_999)}"));
        }
        return rows;
    }

    /// <summary>Regions, countries and cities, with a population summed up the tree.</summary>
    public static List<PlaceNode> Places()
    {
        Random rnd = new(7);
        int id = 0;
        List<PlaceNode> roots = [];
        foreach (string region in new[] { "Europe", "Americas", "Asia" })
        {
            int regionId = ++id;
            List<PlaceNode> countries = [];
            foreach (string country in new[] { "North", "South", "East", "West" })
            {
                int countryId = ++id;
                List<PlaceNode> cities = [];
                for (int k = 1; k <= 3; k++)
                    cities.Add(new PlaceNode(++id, $"City {k} of {country} {region}", 2, countryId, rnd.Next(10_000, 2_000_000),
                        Math.Round(rnd.NextDouble() * 900 + 20, 1), $"Centre {k}", Currencies[rnd.Next(Currencies.Length)], $"UTC+{rnd.Next(0, 12)}", rnd.Next(800, 1950), $"C{id:D3}"));
                PlaceNode c = new(countryId, $"{country} {region}", 1, regionId, cities.Sum(x => x.Population),
                    Math.Round(cities.Sum(x => x.Area) * 10, 1), cities[0].Name, cities[0].Currency, cities[0].Timezone, cities.Min(x => x.Founded), $"N{countryId:D3}");
                c.Children.AddRange(cities);
                countries.Add(c);
            }
            PlaceNode r = new(regionId, region, 0, null, countries.Sum(x => x.Population),
                Math.Round(countries.Sum(x => x.Area), 1), countries[0].Capital, countries[0].Currency, countries[0].Timezone, countries.Min(x => x.Founded), $"R{regionId:D3}");
            r.Children.AddRange(countries);
            roots.Add(r);
        }
        return roots;
    }
}
