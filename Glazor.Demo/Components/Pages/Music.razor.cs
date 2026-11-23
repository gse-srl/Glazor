using Glazor.Demo.Data;
using Glazor.EntityFrameworkCore;
using Glazor.Grds.Mdls;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace Glazor.Demo.Components.Pages;

public partial class Music : IDisposable
{
    [Inject] private IDbContextFactory<ChinookContext> DbFactory { get; init; } = null!;

    [CascadingParameter] public GuiHelper Gui { get; set; } = null!;

    private EFFolderModel<ChinookContext, MusicNode> _model = null!;

    protected override void OnInitialized()
    {
        _model = new()
        {
            DbContextFactory = DbFactory,
            MinRowsToRead = 50,
            ChildrenPageSize = 25,
            ItemKeyExpr = n => n.Id,
            IsFolderFn = n => n.Kind != "Track" && n.Items != 0,   // an artist without albums shows no expander
            // a folder of the page's own above the artists, which no table holds
            VirtualChildrenFn = parent => parent == null
                ? [new MusicNode { Id = MusicNode.AllGenres, Kind = "Genres", Name = "All genres" }]
                : [],
            MoreRowFn = folder => new MusicNode { Id = -folder.Id, Kind = "More", Name = "More…" },
            RootsQueryFn = db => db.Artists.AsNoTracking()
                .Select(a => new MusicNode
                {
                    Id = a.ArtistId,
                    Kind = "Artist",
                    Name = a.Name ?? "",
                    Items = db.Albums.Count(al => al.ArtistId == a.ArtistId),
                })
                .OrderBy(n => n.Name),   // last, so that the model pages over it
            ChildrenQueryFn = (db, parent) => parent.Kind switch
            {
                "Artist" => db.Albums.AsNoTracking()
                    .Where(al => al.ArtistId == parent.Id)
                    .Select(al => new MusicNode
                    {
                        Id = MusicNode.AlbumBase + al.AlbumId,
                        Kind = "Album",
                        Name = al.Title,
                        Items = db.Tracks.Count(t => t.AlbumId == al.AlbumId),
                    })
                    .OrderBy(n => n.Name),
                "Album" => Tracks(db.Tracks.Where(t => t.AlbumId == parent.Id - MusicNode.AlbumBase), MusicNode.TrackBase),
                "Genres" => db.Genres.AsNoTracking()
                    .Select(g => new MusicNode
                    {
                        Id = MusicNode.GenreBase + g.GenreId,
                        Kind = "Genre",
                        Name = g.Name ?? "",
                        Items = db.Tracks.Count(t => t.GenreId == g.GenreId),
                    })
                    .OrderBy(n => n.Name),
                _ => Tracks(db.Tracks.Where(t => t.GenreId == parent.Id - MusicNode.GenreBase), MusicNode.GenreTrackBase),
            },
        };
    }

    // A track under a genre has an id of its own, apart from the same track under its album
    private static IQueryable<MusicNode> Tracks(IQueryable<Track> tracks, int idBase) => tracks.AsNoTracking()
        .OrderBy(t => t.TrackId)
        .Select(t => new MusicNode
        {
            Id = idBase + t.TrackId,
            Kind = "Track",
            Name = t.Name,
            Composer = t.Composer,
            Milliseconds = t.Milliseconds,
            UnitPrice = t.UnitPrice,
        });

    // The label in the tree column: folders in bold, the "More…" row in italics
    private static string LabelStyle(MusicNode node, ITreeNode treeNode) =>
        node.Kind == "More" ? "font-style: italic;" : treeNode.IsLeaf() ? "" : "font-weight: 600;";

    public void Dispose() => _model.Dispose();
}
