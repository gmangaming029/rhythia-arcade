using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public partial class RepositoryBrowser : Window
{
    private readonly List<RepositoryChart> charts = [];
    private LineEdit searchInput;
    private LineEdit creatorInput;
    private LineEdit difficultyInput;
    private LineEdit tagInput;
    private OptionButton sortOption;
    private ItemList chartList;
    private Label detailsLabel;
    private Label statusLabel;
    private Label pageLabel;
    private Button signInButton;
    private Button searchButton;
    private Button installButton;
    private Button previousButton;
    private Button nextButton;
    private RepositoryClient client;
    private RepositoryOAuthSession session;
    private bool busy;
    private int page = 1;
    private int totalCharts;
    private const int PageSize = 24;

    public override void _Ready()
    {
        Title = "Rhythia Chart Repository";
        Size = new Vector2I(1000, 680);
        MinSize = new Vector2I(740, 500);
        CloseRequested += Hide;
        BuildUi();
        Hide();
        signInButton.Disabled = !RepositoryOAuth.IsConfigured;
        statusLabel.Text = RepositoryOAuth.IsConfigured
            ? "Sign in to browse the repository."
            : "Configure the repository OIDC authority, client ID, and API scope to enable sign-in.";
    }

    public void Open()
    {
        PopupCentered();
        if (session?.IsValid == true && client is not null)
            _ = BrowseAsync();
    }

    public void Toggle()
    {
        if (Visible)
            Hide();
        else
            Open();
    }

    public override void _ExitTree()
    {
        client?.Dispose();
    }

    private void BuildUi()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_top", 18);
        margin.AddThemeConstantOverride("margin_right", 20);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        AddChild(margin);

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 12);
        margin.AddChild(layout);

        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 8);
        layout.AddChild(toolbar);

        searchInput = new LineEdit
        {
            PlaceholderText = "Search title or tags",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        searchInput.TextSubmitted += text => SearchFromFirstPage();
        toolbar.AddChild(searchInput);

        sortOption = new OptionButton();
        sortOption.AddItem("Recently updated", 0);
        sortOption.AddItem("Most downloaded", 1);
        sortOption.ItemSelected += index => SearchFromFirstPage();
        toolbar.AddChild(sortOption);

        searchButton = new Button { Text = "Search" };
        searchButton.Pressed += SearchFromFirstPage;
        toolbar.AddChild(searchButton);

        signInButton = new Button { Text = "Sign in" };
        signInButton.Pressed += async () => await ToggleSignInAsync();
        toolbar.AddChild(signInButton);

        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 8);
        layout.AddChild(filters);

        creatorInput = new LineEdit
        {
            PlaceholderText = "Creator",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        filters.AddChild(creatorInput);

        difficultyInput = new LineEdit
        {
            PlaceholderText = "Difficulty",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        filters.AddChild(difficultyInput);

        tagInput = new LineEdit
        {
            PlaceholderText = "Tags (comma separated)",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        filters.AddChild(tagInput);

        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 16);
        body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        layout.AddChild(body);

        chartList = new ItemList
        {
            CustomMinimumSize = new Vector2(420, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single
        };
        chartList.ItemSelected += OnChartSelected;
        body.AddChild(chartList);

        var details = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(340, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        details.AddThemeConstantOverride("separation", 12);
        body.AddChild(details);

        detailsLabel = new Label
        {
            Text = "Select a chart to view details.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        details.AddChild(detailsLabel);

        installButton = new Button { Text = "Download and install", Disabled = true };
        installButton.Pressed += async () => await InstallSelectedAsync();
        details.AddChild(installButton);

        var pagination = new HBoxContainer();
        pagination.AddThemeConstantOverride("separation", 8);
        layout.AddChild(pagination);

        previousButton = new Button { Text = "Previous", Disabled = true };
        previousButton.Pressed += async () =>
        {
            if (page > 1)
            {
                page--;
                await BrowseAsync();
            }
        };
        pagination.AddChild(previousButton);

        pageLabel = new Label
        {
            Text = "Page 1",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        pagination.AddChild(pageLabel);

        nextButton = new Button { Text = "Next", Disabled = true };
        nextButton.Pressed += async () =>
        {
            if (page * PageSize < totalCharts)
            {
                page++;
                await BrowseAsync();
            }
        };
        pagination.AddChild(nextButton);

        statusLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 28)
        };
        layout.AddChild(statusLabel);
    }

    private async Task ToggleSignInAsync()
    {
        if (session?.IsValid == true)
        {
            client?.Dispose();
            client = null;
            session = null;
            charts.Clear();
            page = 1;
            totalCharts = 0;
            chartList.Clear();
            detailsLabel.Text = "Select a chart to view details.";
            installButton.Disabled = true;
            previousButton.Disabled = true;
            nextButton.Disabled = true;
            pageLabel.Text = "Page 1";
            signInButton.Text = "Sign in";
            statusLabel.Text = "Signed out.";
            return;
        }

        SetBusy(true, "Opening secure sign-in in your browser...");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            session = await RepositoryOAuth.SignInAsync(timeout.Token);
            client?.Dispose();
            client = new RepositoryClient(
                RepositoryClient.GetConfiguredApiBaseUri(),
                _ => Task.FromResult(session?.IsValid == true ? session.AccessToken : null));
            signInButton.Text = "Sign out";
            await BrowseAsync();
        }
        catch (Exception exception)
        {
            session = null;
            client?.Dispose();
            client = null;
            statusLabel.Text = $"Sign-in failed: {exception.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task BrowseAsync()
    {
        if (client is null || session?.IsValid != true)
        {
            statusLabel.Text = "Sign in before browsing charts.";
            return;
        }

        SetBusy(true, "Loading charts...");
        try
        {
            string sort = sortOption.Selected == 1 ? "popular" : "updated";
            RepositoryPage<RepositoryChart> result = await client.BrowseAsync(
                query: searchInput.Text.Trim(),
                creator: creatorInput.Text.Trim(),
                difficulty: difficultyInput.Text.Trim(),
                tags: ParseTags(tagInput.Text),
                sort: sort,
                page: page,
                pageSize: PageSize);
            charts.Clear();
            charts.AddRange(result.Items);
            chartList.Clear();
            foreach (RepositoryChart chart in charts)
                chartList.AddItem($"{chart.Title}  —  {chart.Creator}  ·  {chart.Difficulty}");
            totalCharts = result.Total;
            pageLabel.Text = $"Page {result.Page} · {result.Total} charts";
            previousButton.Disabled = busy || page <= 1;
            nextButton.Disabled = busy || page * PageSize >= totalCharts;
            statusLabel.Text = $"Showing {charts.Count} of {totalCharts} charts.";
            installButton.Disabled = true;
            if (charts.Count == 0)
                detailsLabel.Text = "No charts match this search.";
        }

        catch (Exception exception)
        {
            statusLabel.Text = $"Could not load charts: {exception.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SearchFromFirstPage()
    {
        page = 1;
        _ = BrowseAsync();
    }

    private void OnChartSelected(long index)
    {
        if (index < 0 || index >= charts.Count)
            return;
        RepositoryChart chart = charts[(int)index];
        detailsLabel.Text =
            $"{chart.Title}\nby {chart.Creator}\n\n{chart.Description}\n\n" +
            $"Difficulty: {chart.Difficulty}\n" +
            $"Tags: {string.Join(", ", chart.Tags)}\n" +
            $"Ranked status: {chart.RankedState}\n" +
            $"Updated: {chart.UpdatedAt.ToLocalTime():g}\n" +
            $"Package: {FormatSize(chart.SizeBytes)}";
        installButton.Disabled = busy || session?.IsValid != true;
    }

    private async Task InstallSelectedAsync()
    {
        int selectedIndex = chartList.GetSelectedItems().Length > 0
            ? (int)chartList.GetSelectedItems()[0]
            : -1;
        if (client is null || session?.IsValid != true || selectedIndex < 0 || selectedIndex >= charts.Count)
        {
            statusLabel.Text = "Sign in and select a chart before installing.";
            return;
        }

        RepositoryChart chart = charts[selectedIndex];
        SetBusy(true, $"Downloading and verifying {chart.Title}...");
        try
        {
            string downloadDirectory = Path.Combine(Constants.USER_FOLDER, "repository-downloads");
            await client.InstallPhxmPackageAsync(chart, downloadDirectory);
            statusLabel.Text = $"{chart.Title} was verified and imported into your chart library.";
        }
        catch (NotSupportedException exception)
        {
            statusLabel.Text = $"Downloaded, but this chart format cannot be installed in-game: {exception.Message}";
        }
        catch (Exception exception)
        {
            string detail = exception.InnerException?.Message ?? exception.Message;
            statusLabel.Text = $"Could not install {chart.Title}: {detail}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool value, string message = "")
    {
        busy = value;
        searchButton.Disabled = busy || client is null || session?.IsValid != true;
        signInButton.Disabled = busy || !RepositoryOAuth.IsConfigured;
        previousButton.Disabled = busy || page <= 1;
        nextButton.Disabled = busy || page * PageSize >= totalCharts;
        installButton.Disabled = busy || chartList.GetSelectedItems().Length == 0 ||
            client is null || session?.IsValid != true;
        if (!string.IsNullOrEmpty(message))
            statusLabel.Text = message;
    }

    private static string FormatSize(long sizeBytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = sizeBytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    private static string[] ParseTags(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
