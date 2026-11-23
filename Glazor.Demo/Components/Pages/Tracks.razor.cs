using Glazor.Demo.Data;
using Glazor.EntityFrameworkCore;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Glazor.Demo.Components.Pages;

public partial class Tracks : IDisposable
{
    [Inject] private IDbContextFactory<ChinookContext> DbFactory { get; init; } = null!;

    [CascadingParameter] public GuiHelper Gui { get; set; } = null!;

    private EditableEFDataGridModel<ChinookContext, TrackRow> _model = null!;

    // Set filters over the values their columns have in the grid's query
    private EFDistinctDef<ChinookContext, TrackRow, string?> _genreFDef = null!, _mediaTypeFDef = null!;
    private EFDistinctDef<ChinookContext, TrackRow, double> _priceFDef = null!;

    private sealed record Choice(int Id, string Name);
    private List<Choice> _albums = [], _genres = [], _mediaTypes = [];

    // The current row, shown; while an edit or an insert is pending, the row the model handed out, which the form changes
    private TrackRow _row = new();
    private EditContext _editContext = null!;
    private InputText? _nameInput;
    private bool _focus;

    private bool Inserting => _model.IsInsertPending();
    private bool Editing => _model.IsEditPending() || Inserting;

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
        _priceFDef = ValuesOf<double>(t => t.UnitPrice);
        _editContext = new(_row);
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focus && _nameInput?.Element is ElementReference name)
        {
            _focus = false;
            await name.FocusAsync();
        }
    }

    private void SetRow(TrackRow row)
    {
        _row = row;
        _editContext = new(_row);
    }

    // The panel follows the current row while nothing is being edited
    private Task ShowAsync(TrackRow? row)
    {
        if (!Editing)
            SetRow(row ?? new());
        return Task.CompletedTask;
    }

    private async Task StartEditAsync(int rowIdx)
    {
        if (Editing)
            return;
        _model.SetCurrentRow(rowIdx);
        SetRow(await _model.EditAsync());
        _focus = true;
    }

    private async Task NewAsync()
    {
        if (Editing)
            return;
        TrackRow? current = _model.GetCurrentItem();
        SetRow(await _model.InsertAsync(new TrackRow
        {
            AlbumId = current?.AlbumId,   // a new track starts on the current one's album, genre and media
            GenreId = current?.GenreId,
            MediaTypeId = current?.MediaTypeId ?? _mediaTypes.FirstOrDefault()?.Id ?? 1,
            UnitPrice = current?.UnitPrice ?? 0.99,
        }, afterCurrentRow: true));
        _focus = true;
    }

    private async Task SaveAsync()
    {
        try
        {
            await using ChinookContext db = await DbFactory.CreateDbContextAsync();
            Track track = new()
            {
                TrackId = _row.TrackId,
                Name = _row.Name,
                AlbumId = _row.AlbumId,
                MediaTypeId = _row.MediaTypeId,
                GenreId = _row.GenreId,
                Composer = string.IsNullOrWhiteSpace(_row.Composer) ? null : _row.Composer,
                Milliseconds = _row.Milliseconds,
                Bytes = _row.Bytes,
                UnitPrice = _row.UnitPrice,
            };
            // The row as read, not the track as it is now: a concurrency token would be compared with the row's own value
            if (Inserting)
                db.Tracks.Add(track);
            else
                db.Tracks.Update(track);
            await db.SaveChangesAsync();
            _row.TrackId = track.TrackId;   // a new row's generated key, by which PostAsync finds it again
            await _model.PostAsync();   // reads the row back, as the database now has it
            await ShowAsync(_model.GetCurrentItem());
        }
        catch (DbUpdateConcurrencyException e)
        {
            await CancelAsync();   // reads the row back as another user left it, or drops it if they deleted it
            await Gui.Error(e, "Another user changed or deleted the track: it is shown as the database has it now.");
        }
        catch (DbUpdateException e)
        {
            await Gui.Error(e, "The database refused the change, which was not saved.");
        }
    }

    private async Task CancelAsync()
    {
        if (!Editing)
            return;
        await _model.CancelAsync();
        await ShowAsync(_model.GetCurrentItem());
    }

    public void Dispose() => _model.Dispose();
}
