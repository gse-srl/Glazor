using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Glazor.Demo.Data;

/// <summary>
/// The Sakila sample database, a DVD rental store: 16,044 rentals over 599 customers, 1,000 films, their 200 actors and
/// 16,049 payments, in the SQLite build of https://github.com/bradleygrant/sakila-sqlite3 (BSD 3-Clause), with an index on
/// payment.rental_id added: without it every row's amount scans the payments. Only the tables
/// the demo reads are mapped, their snake_case names included. Money is read as doubles: SQLite stores decimals as text, which EF Core
/// cannot sort or compare in SQL.
/// </summary>
public class SakilaContext(DbContextOptions<SakilaContext> options) : DbContext(options)
{
    public DbSet<Rental> Rentals => Set<Rental>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Actor> Actors => Set<Actor>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<FilmCategory>().HasKey(fc => new { fc.FilmId, fc.CategoryId });
        b.Entity<FilmActor>().HasKey(fa => new { fa.FilmId, fa.ActorId });
    }
}

[Table("rental")]
public class Rental
{
    [Key, Column("rental_id")] public int RentalId { get; set; }
    [Column("rental_date")] public DateTime RentalDate { get; set; }
    [Column("inventory_id")] public int InventoryId { get; set; }
    public Inventory Inventory { get; set; } = null!;
    [Column("customer_id")] public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    [Column("return_date")] public DateTime? ReturnDate { get; set; }
    [Column("staff_id")] public int StaffId { get; set; }
    public Staff Staff { get; set; } = null!;
}

[Table("inventory")]
public class Inventory
{
    [Key, Column("inventory_id")] public int InventoryId { get; set; }
    [Column("film_id")] public int FilmId { get; set; }
    public Film Film { get; set; } = null!;
    [Column("store_id")] public int StoreId { get; set; }
}

[Table("film")]
public class Film
{
    [Key, Column("film_id")] public int FilmId { get; set; }
    [Column("title")] public string Title { get; set; } = "";
    [Column("release_year")] public string? ReleaseYear { get; set; }
    [Column("rental_rate")] public double RentalRate { get; set; }
    [Column("length")] public int? Length { get; set; }
    [Column("rating")] public string? Rating { get; set; }
    public List<FilmCategory> Categories { get; } = [];
    public List<FilmActor> Actors { get; } = [];
}

[Table("film_actor")]
public class FilmActor
{
    [Column("film_id")] public int FilmId { get; set; }
    [Column("actor_id")] public int ActorId { get; set; }
    public Actor Actor { get; set; } = null!;
}

[Table("actor")]
public class Actor
{
    [Key, Column("actor_id")] public int ActorId { get; set; }
    [Column("first_name")] public string FirstName { get; set; } = "";
    [Column("last_name")] public string LastName { get; set; } = "";
    public List<FilmActor> Films { get; } = [];
}

[Table("film_category")]
public class FilmCategory
{
    [Column("film_id")] public int FilmId { get; set; }
    [Column("category_id")] public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

[Table("category")]
public class Category
{
    [Key, Column("category_id")] public int CategoryId { get; set; }
    [Column("name")] public string Name { get; set; } = "";
}

[Table("customer")]
public class Customer
{
    [Key, Column("customer_id")] public int CustomerId { get; set; }
    [Column("first_name")] public string FirstName { get; set; } = "";
    [Column("last_name")] public string LastName { get; set; } = "";
    [Column("email")] public string? Email { get; set; }
    [Column("store_id")] public int StoreId { get; set; }
}

[Table("staff")]
public class Staff
{
    [Key, Column("staff_id")] public int StaffId { get; set; }
    [Column("first_name")] public string FirstName { get; set; } = "";
    [Column("last_name")] public string LastName { get; set; } = "";
}

[Table("payment")]
public class Payment
{
    [Key, Column("payment_id")] public int PaymentId { get; set; }
    [Column("rental_id")] public int? RentalId { get; set; }
    [Column("amount")] public double Amount { get; set; }
    [Column("payment_date")] public DateTime PaymentDate { get; set; }
}

/// <summary>An actor as the rentals' cast filter lists it, with the name in title case, projected in SQL.</summary>
public sealed record ActorRow(int Id, string Name, int Films)
{
    public static IQueryable<ActorRow> Query(SakilaContext db) => db.Actors.Select(a => new ActorRow(a.ActorId,
        a.FirstName.Substring(0, 1) + a.FirstName.Substring(1).ToLower() + " " + a.LastName.Substring(0, 1) + a.LastName.Substring(1).ToLower(),
        a.Films.Count));
}

/// <summary>What the rentals grid shows: one rental with its customer, film and payment, projected in SQL.</summary>
public sealed record RentalRow
{
    public int RentalId { get; init; }
    public DateTime RentalDate { get; init; }
    public DateTime? ReturnDate { get; init; }
    public string Customer { get; init; } = "";
    public string? Email { get; init; }
    public string Film { get; init; } = "";
    public string? Category { get; init; }
    public List<int> Cast { get; init; } = [];   // the film's actors, by id
    public string? Rating { get; init; }
    public int? Length { get; init; }
    public double RentalRate { get; init; }
    public double? Amount { get; init; }
    public string Staff { get; init; } = "";
    public int Store { get; init; }
}
