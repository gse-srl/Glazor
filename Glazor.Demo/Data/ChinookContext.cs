using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Glazor.Demo.Data;

/// <summary>
/// The Chinook sample database (https://github.com/lerocha/chinook-database, MIT license): a music store, 3,503 tracks over
/// albums, artists, genres and media types. Only the tables the demo reads are mapped. The prices are read as doubles:
/// SQLite stores decimals as text, which EF Core cannot sort or compare in SQL.
/// </summary>
public class ChinookContext(DbContextOptions<ChinookContext> options) : DbContext(options)
{
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<MediaType> MediaTypes => Set<MediaType>();
}

[Table("Track")]
public class Track
{
    [Key] public int TrackId { get; set; }
    public string Name { get; set; } = "";
    public int? AlbumId { get; set; }
    public Album? Album { get; set; }
    public int MediaTypeId { get; set; }
    public MediaType MediaType { get; set; } = null!;
    public int? GenreId { get; set; }
    public Genre? Genre { get; set; }
    public string? Composer { get; set; }
    public int Milliseconds { get; set; }
    public int? Bytes { get; set; }
    public double UnitPrice { get; set; }
}

[Table("Album")]
public class Album
{
    [Key] public int AlbumId { get; set; }
    public string Title { get; set; } = "";
    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
}

[Table("Artist")]
public class Artist
{
    [Key] public int ArtistId { get; set; }
    public string? Name { get; set; }
}

[Table("Genre")]
public class Genre
{
    [Key] public int GenreId { get; set; }
    public string? Name { get; set; }
}

[Table("MediaType")]
public class MediaType
{
    [Key] public int MediaTypeId { get; set; }
    public string? Name { get; set; }
}

/// <summary>
/// What the paged grid shows: a flat row projected in SQL from a track and its navigations. The edit panel edits the row
/// itself, as the model hands it out, so it carries every column of the track and is a class, not a record whose equality
/// would follow the edits.
/// </summary>
public sealed class TrackRow
{
    public int TrackId { get; set; }   // set on a new row once saved: the model finds a posted insert by its key
    [Required] public string Name { get; set; } = "";
    public int? AlbumId { get; set; }
    public int MediaTypeId { get; set; }
    public int? GenreId { get; set; }
    public string? Composer { get; set; }
    [Range(1, int.MaxValue)] public int Milliseconds { get; set; }
    [Range(0, int.MaxValue)] public int? Bytes { get; set; }
    [Range(0, 100)] public double UnitPrice { get; set; }
    // shown only, read back after a save
    public string? Album { get; init; }
    public string? Artist { get; init; }
    public string? Genre { get; init; }
    public string? MediaType { get; init; }
}

/// <summary>
/// A row of the folder tree: an artist, one of its albums or one of an album's tracks, the virtual folder of all genres, a
/// genre or one of its tracks, projected in SQL to one shape. The id is unique across them: an artist's own, the others
/// offset by a base each, and the row below a page of children the negative of its folder's.
/// </summary>
public sealed record MusicNode
{
    public const int AlbumBase = 100_000, GenreBase = 200_000, AllGenres = 300_000, TrackBase = 1_000_000, GenreTrackBase = 2_000_000;

    public int Id { get; init; }
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public int? Items { get; init; }   // an artist's albums, an album's tracks
    public string? Composer { get; init; }
    public int? Milliseconds { get; init; }
    public double? UnitPrice { get; init; }
}
