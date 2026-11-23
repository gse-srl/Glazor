using Glazor.Demo.Data;
using Glazor.EntityFrameworkCore;
using Glazor.Grds;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Glazor.Demo.Components.Pages;

public partial class TracksInPlace : IDisposable
{
    [Inject] private IDbContextFactory<ChinookContext> DbFactory { get; init; } = null!;

    [CascadingParameter] public GuiHelper Gui { get; set; } = null!;

    private EditableEFDataGridModel<ChinookContext, TrackRow> _model = null!;
    private EditMode _mode = EditMode.Manual;

    // Set filters over the values their columns have in the grid's query
    private EFDistinctDef<ChinookContext, TrackRow, string?> _genreFDef = null!, _mediaTypeFDef = null!;

    private sealed record Choice(int Id, string Name);
    private List<Choice> _albums = [], _genres = [], _mediaTypes = [];

    protected override async Task OnInitializedAsync()
    {
        _model = new()
        {
            DbContextFactory = DbFactory,
            ItemKeyExpr = t => t.TrackId,
            BaseQueryFn = (db, _) => Rows(db),
        };
        _genreFDef = ValuesOf<string?>(t => t.Genre);
        _mediaTypeFDef = ValuesOf<string?>(t => t.MediaType);
        await using ChinookContext db = await DbFactory.CreateDbContextAsync();
        _albums = await db.Albums.OrderBy(a => a.Title).Select(a => new Choice(a.AlbumId, a.Title + " (" + a.Artist.Name + ")")).ToListAsync();
        _genres = await db.Genres.OrderBy(g => g.Name).Select(g => new Choice(g.GenreId, g.Name ?? "")).ToListAsync();
        _mediaTypes = await db.MediaTypes.OrderBy(m => m.Name).Select(m => new Choice(m.MediaTypeId, m.Name ?? "")).ToListAsync();
    }

    // The grid's rows, which the set filters read their values from too
    private static IQueryable<TrackRow> Rows(ChinookContext db) => db.Tracks.AsNoTracking().Select(t => new TrackRow
    {
        TrackId = t.TrackId,
        Name = t.Name,
        AlbumId = t.AlbumId,
        MediaTypeId = t.MediaTypeId,
        GenreId = t.GenreId,
        Composer = t.Composer,
        Milliseconds = t.Milliseconds,
        Bytes = t.Bytes,
        UnitPrice = t.UnitPrice,
        Album = t.Album!.Title,
        Artist = t.Album!.Artist.Name,
        Genre = t.Genre!.Name,
        MediaType = t.MediaType.Name,
    });

    private EFDistinctDef<ChinookContext, TrackRow, VT> ValuesOf<VT>(Expression<Func<TrackRow, VT?>> column) => new(startFull: true)
    {
        DbContextFactoryFn = () => DbFactory,
        QueryFn = Rows,
        ColumnExpr = column,
    };

    // A new track starts on the current one's album, genre and media
    private TrackRow NewTrack(TrackRow? current) => new()
    {
        AlbumId = current?.AlbumId,
        GenreId = current?.GenreId,
        MediaTypeId = current?.MediaTypeId ?? _mediaTypes.FirstOrDefault()?.Id ?? 1,
        UnitPrice = current?.UnitPrice ?? 0.99,
    };

    private async Task SaveAsync(GridPost<TrackRow> post)
    {
        TrackRow row = post.Item;
        try
        {
            await using ChinookContext db = await DbFactory.CreateDbContextAsync();
            Track track = new()
            {
                TrackId = row.TrackId,
                Name = row.Name,
                AlbumId = row.AlbumId,
                MediaTypeId = row.MediaTypeId,
                GenreId = row.GenreId,
                Composer = string.IsNullOrWhiteSpace(row.Composer) ? null : row.Composer,
                Milliseconds = row.Milliseconds,
                Bytes = row.Bytes,
                UnitPrice = row.UnitPrice,
            };
            // The row as read, not the track as it is now: a concurrency token would be compared with the row's own value
            if (post.Inserting)
                db.Tracks.Add(track);
            else
                db.Tracks.Update(track);
            await db.SaveChangesAsync();
            row.TrackId = track.TrackId;   // a new row's generated key, by which PostAsync finds it again
            await _model.PostAsync();   // reads the row back, as the database now has it
        }
        catch (DbUpdateConcurrencyException e)
        {
            await _model.CancelAsync();   // reads the row back as another user left it, or drops it if they deleted it
            await Gui.Error(e, "Another user changed or deleted the track: it is shown as the database has it now.");
        }
        catch (DbUpdateException e)
        {
            await Gui.Error(e, "The database refused the change, which was not saved.");   // the row stays in edit
        }
    }

    private Task RemoveAsync(TrackRow row) => Gui.OpenConfirm("Remove track", $"Remove the track \"{row.Name}\"?", async () =>
    {
        try
        {
            await using ChinookContext db = await DbFactory.CreateDbContextAsync();
            db.Tracks.Remove(new Track { TrackId = row.TrackId, Name = row.Name });
            await db.SaveChangesAsync();
            await _model.ReloadItemAsync(_model.GetCurrentRow());   // finds the row gone and drops it
            StateHasChanged();   // the OK is the layout's event, which does not render this page and its grid
        }
        catch (DbUpdateException e)
        {
            await Gui.Error(e, "The track is on an invoice or a playlist, so it cannot be removed.");
        }
    });

    public void Dispose() => _model.Dispose();
}
