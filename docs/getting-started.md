# Getting started

## 1. Install

```bash
dotnet add package Glazor
```

The paged models over an EF Core query are a second package, which brings EF Core 10 or later with it; the grids and the
in-memory models do not need it:

```bash
dotnet add package Glazor.EntityFrameworkCore
```

The repository's `Glazor.Demo` project is a Blazor Web App set up as the steps below describe, with the grids and the
main components on its pages and a dark theme switch: `dotnet run --project Glazor.Demo` and open the address it prints.

## 2. Set up App.razor

Glazor ships its CSS as a static web asset, and needs the app to be interactive from the root. In `App.razor`
(Blazor Web App), link the stylesheet and set the render mode on `HeadOutlet` and `Routes`:

```razor
<head>
    ...
    <link href="_content/Glazor/glazor.css" rel="stylesheet" />
    @* optional, the dark theme (or your own, see Architecture › Theming):
    <link href="_content/Glazor/glazor-dark.css" rel="stylesheet" /> *@
    <HeadOutlet @rendermode="InteractiveServer" />
</head>
<body>
    <Routes @rendermode="InteractiveServer" />
    ...
</body>
```

`dotnet new blazor --interactivity Server --all-interactive` creates it this way. A `@rendermode` on each page
is not enough: the main layout then renders statically, and the spinner a paged grid needs lives there (step 6).

With a paged grid, turn prerendering off on both: `@rendermode="new InteractiveServerRenderMode(prerender: false)"`.
The grid fetches its first rows during its first render and shows the spinner while it does, which needs the circuit;
under prerendering that render runs once more on the static pass, where the spinner's interop fails and is logged, and
the query runs twice. The in-memory models do not fetch, so a page with only those may keep prerendering.

The spinner is in the layout, so it shows only once the layout is in the browser. A page opened by a link or a reload is
the circuit's first: nothing reaches the browser until its first render ends, its grids' queries included, and the page
stays blank meanwhile. `AppLoading` (`Glazor.Misc`), in `App.razor` outside `Routes`, fills that wait with the spinner's
wheel, and `glazor.css` hides it once the layout's `Spinner` is in the page:

```razor
<body>
    <AppLoading />
    <Routes @rendermode="new InteractiveServerRenderMode(prerender: false)" />
    <div id="blazor-error-ui">...</div>
    ...
</body>
```

Keep the template's `#blazor-error-ui` there too, in `App.razor`, and not in the layout, where the template puts it. With
prerendering off the layout comes with the circuit's first render: when that render throws, the layout never reaches the
browser, Blazor finds no error bar to show, and the wheel turns for good. In `App.razor` the bar is in the page from the
start, and `glazor.css` hides the wheel when Blazor shows it. Its styles then move from the layout's scoped CSS to a
stylesheet that reaches `App.razor`.

With individual accounts the template sets the render mode only where `HttpContext.AcceptsInteractiveRouting()`,
leaving the account pages marked `[ExcludeFromInteractiveRouting]` static; a page with a grid must not be excluded.

The grid's own styles are scoped CSS (`Grigliaria.razor.css` and friends), which Blazor bundles into
`_content/Glazor/Glazor.bundle.scp.css` and pulls in through the host application's `{App}.styles.css`. The
default Blazor templates already link that file; if your `<head>` does not have
`<link rel="stylesheet" href="YourApp.styles.css" />`, the grid renders unstyled and nothing reports an error.

## 3. Enable logging inside the library

A few Glazor types are plain classes rather than components, so they can't take an `ILogger` through
dependency injection. Hand them the factory once at startup and they'll log through your normal pipeline;
skip this and they fall back to `NullLogger` and stay silent.

```csharp
// Program.cs, after the app is built
var app = builder.Build();

Glazor.Misc.LoggerSupport.SetLoggerFactory(app.Services.GetRequiredService<ILoggerFactory>());
```

You can then control their verbosity like any other category:

```json
{
  "Logging": {
    "LogLevel": {
      "Glazor": "Warning"
    }
  }
}
```

While you are in `appsettings.json`: Glazor reads exactly one setting of its own,
`Grigliaria:TouchScrollerEnabled`, which sets the application-wide default for the grid parameter of
the same name — see
[Grigliaria › Configuring the touch scroller](grigliaria.md#configuring-the-touch-scroller-application-wide).

## 4. Add the namespaces

In `_Imports.razor`:

```razor
@using Glazor.Grds
@using Glazor.Grds.Decs
@using Glazor.Misc
@using Glazor.Tabs
```

`Glazor.Grds.Decs` holds the cell components a template reuses, `DefaultCell`, `DefaultTreeCell` and `DefaultMultiSelCell` among
them: without it a template that names one builds with only warning RZ10012, and the cell renders empty.

`Glazor.Grds.Mdls` (models) and `Glazor.EntityFrameworkCore` (the EF Core models, named like their package) are usually imported per-file in
code-behind rather than globally.

The components' own texts follow `CultureInfo.CurrentUICulture`: English, or Italian when the UI culture is Italian.
A circuit has the server's culture unless the app sets it from the request; see
[Architecture › Localization](Technical_Documentation.md#localization) for request localization and for supplying
your own texts.

## 5. Your first grid

A grid is two pieces: a **model** that supplies rows, and the **`Grigliaria`** component that renders them.
The simplest model is an in-memory list.

```razor
@* Components/Pages/Products.razor *@
@page "/products"

<div class="my-grid">
    <Grigliaria TGridItem="Product" Model="_model"
                RowHeight="40" HeaderHeight="50"
                VertResizer="@(GridVertResizers.RowHeight("height: 60vh;", 4, 30))">
        <GridColumn Property="@(x => x.Code)"  Name="Code"  Label="Code" PreferredSize="10" />
        <GridColumn Property="@(x => x.Name)"  Name="Name"  Label="Name" PreferredSize="30" />
        <GridColumn Property="@(x => x.Price)" Name="Price" Label="Price" Alignment="Alignment.Right" />
    </Grigliaria>
</div>
```

```csharp
// Components/Pages/Products.razor.cs
using Glazor.Grds.Mdls;

namespace MyApp.Components.Pages;   // the namespace of Products.razor: your project's, then its folders

public partial class Products
{
    private readonly DataGridModel<Product> _model = new()
    {
        ItemKeyFn = p => p.Id,
    };

    protected override void OnInitialized()
    {
        _model.GetData().Set(LoadProducts());
    }

    private static List<Product> LoadProducts() =>
    [
        new(1, "A-100", "Bolt", 0.25m),
        new(2, "A-200", "Nut", 0.10m),
        new(3, "B-100", "Washer", 0.05m),
    ];

    public record Product(int Id, string Code, string Name, decimal Price);
}
```

Three things to note:

- **No `@rendermode`** — the grid needs interactivity for sorting, filtering and keyboard navigation, and the
  page has it from `Routes` (step 2).
- **`ItemKeyFn`** — how the model identifies a row. It uses this to keep the current row selected across
  reloads, sorts and filter changes.
- **`VertResizer`** — how many rows to render. `Grigliaria` pages rather than virtualizes, so it needs to know
  how much vertical space it has; the resizer measures a CSS height and converts it to a page size. Without
  one the grid falls back to its `PageSize` default.

## 6. Point it at a database instead

Swap the model. In the markup `Property` becomes `EFProperty`, so filters and sorting can be translated to SQL, and the
grid takes the layout's `Spinner`, which it shows while a query runs.

The model opens a `DbContext` of its own for each query, so register a factory in `Program.cs`, with your provider:

```csharp
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer(connectionString));
```

and inject it in the page:

```csharp
using Glazor.EntityFrameworkCore;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

[Inject] private IDbContextFactory<AppDbContext> DbContextFactory { get; set; } = null!;

private EFDataGridModel<AppDbContext, Product> _model = null!;

protected override void OnInitialized()
{
    _model = new()
    {
        DbContextFactory = DbContextFactory,
        ItemKeyExpr      = x => x.Id,
        BaseQueryFn      = (db, _) => db.Products.AsNoTracking().OrderBy(p => p.Code),
        ReadAll          = false,   // read incrementally
    };
}

public void Dispose() => _model?.Dispose();   // the grid does not: the model holds the rows it spilled to disk until disposed
```

```razor
@implements IDisposable

<Grigliaria TGridItem="Product" Model="_model" Spinner="@Gui.Spinner" ...>
    <GridColumn EFProperty="@(x => x.Code)" Name="Code" Label="Code" />
</Grigliaria>

@code {
    [CascadingParameter] public GuiHelper Gui { get; set; } = null!;   // a class of yours that carries the layout's Spinner, see Architecture › Spinner
}
```

> [!IMPORTANT]
> `EFDataGridModel` fetches rows on demand, which means the grid can block briefly while a query runs.
> It needs a **`Spinner`**: without one the first render throws, and with one the user sees it during longer
> fetches. The spinner is provided by the application, allocated once in the main layout and cascaded to the
> pages, as `Gui` is here, which is why the app is interactive from the root (step 2); it cannot be created in the
> same page as the grid. See
> [Architecture › Spinner](Technical_Documentation.md#spinner) for the layout, and
> [Data models › EFDataGridModel](data-models.md#efdatagridmodeltdbcontext-tgriditem).

## Where next

- **[Grigliaria](grigliaria.md)** — the full parameter reference, column options, filtering, layout
  persistence, templates and keyboard shortcuts.
- **[Data models](data-models.md)** — choosing and configuring a model, and the editing flows.
