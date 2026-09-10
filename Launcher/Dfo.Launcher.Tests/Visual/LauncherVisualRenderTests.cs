using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dfo.Launcher.Api;
using Dfo.Launcher.Configuration;
using Dfo.Launcher.Launching;
using Dfo.Launcher.Preferences;
using Dfo.Launcher.ViewModels;

namespace Dfo.Launcher.Tests.Visual;

public sealed class LauncherVisualRenderTests
{
    private const int LogicalWidth = 760;
    private const int LogicalHeight = 560;

    [Theory]
    [InlineData("login", 96)]
    [InlineData("login", 144)]
    [InlineData("register", 96)]
    [InlineData("register", 144)]
    [InlineData("loading", 96)]
    [InlineData("loading", 144)]
    [InlineData("error", 96)]
    [InlineData("error", 144)]
    public void Required_state_controls_render_without_clipping_at_requested_dpi(
        string state,
        int dpi)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RenderState(state, dpi);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF render thread timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void Api_field_error_is_visible_in_the_editable_real_WPF_form()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var client = new FieldErrorApiClient();
                using var viewModel = new LauncherViewModel(
                    new ValidConfigurationSource(),
                    new SingleApiClientFactory(client),
                    new EmptyPreferencesStore(),
                    new SuccessfulGameLauncher(),
                    NullLauncherEventLog.Instance);
                viewModel.InitializeAsync().GetAwaiter().GetResult();
                viewModel.LoginId = "bad";
                viewModel.SubmitAsync("password-secret").GetAwaiter().GetResult();
                var window = new MainWindow(viewModel);
                var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                root.Measure(new Size(LogicalWidth, LogicalHeight));
                root.Arrange(new Rect(0, 0, LogicalWidth, LogicalHeight));
                root.UpdateLayout();

                var form = Get(window, "FormPanel");
                var fieldError = Assert.IsType<TextBlock>(Get(window, "FieldErrorTextBlock"));
                var loginId = Assert.IsType<TextBox>(Get(window, "LoginIdBox"));
                var submit = Assert.IsType<Button>(Get(window, "PrimaryActionButton"));
                Assert.Equal(Visibility.Visible, form.Visibility);
                Assert.Equal(Visibility.Visible, fieldError.Visibility);
                Assert.Equal("账号格式错误。", fieldError.Text);
                Assert.True(fieldError.ActualHeight > 0);
                Assert.True(submit.IsEnabled);
                loginId.Text = "corrected-player";
                Assert.Equal("corrected-player", viewModel.LoginId);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF field-error test timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void Successful_game_start_closes_the_real_launcher_window()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                using var viewModel = new LauncherViewModel(
                    new ValidConfigurationSource(),
                    new SingleApiClientFactory(new SuccessfulApiClient()),
                    new SuccessfulPreferencesStore(),
                    new SuccessfulGameLauncher(),
                    NullLauncherEventLog.Instance);
                viewModel.InitializeAsync().GetAwaiter().GetResult();
                viewModel.LoginId = "player";
                window = new MainWindow(viewModel);
                window.Show();
                Assert.True(window.IsVisible);

                var password = Assert.IsType<PasswordBox>(Get(window, "PasswordInput"));
                password.Password = "password-secret";
                var submit = Assert.IsType<Button>(Get(window, "PrimaryActionButton"));
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.False(window.IsVisible);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF close-after-launch test timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void RenderState(string state, int dpi)
    {
        var outputDirectory = Environment.GetEnvironmentVariable("TASK05_VISUAL_EVIDENCE_DIR")
            ?? Path.Combine(Path.GetTempPath(), "dfo-launcher-visual-evidence");
        Directory.CreateDirectory(outputDirectory);
        using var viewModel = CreateViewModel();
        viewModel.ApplyVisualPreview(state);
        var window = new MainWindow(viewModel);
        var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        var logicalSize = new Size(LogicalWidth, LogicalHeight);
        root.Measure(logicalSize);
        Assert.True(
            root.DesiredSize.Width <= LogicalWidth && root.DesiredSize.Height <= LogicalHeight,
            $"{state} at {dpi} DPI requests {root.DesiredSize} DIPs.");
        root.Arrange(new Rect(logicalSize));
        root.UpdateLayout();

        Assert.Equal(LogicalWidth, root.ActualWidth, 3);
        Assert.Equal(LogicalHeight, root.ActualHeight, 3);
        AssertRequiredStateControls(window, root, state, dpi);
        AssertVisibleElementsStayInside(root, state, dpi);

        var scale = dpi / 96d;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(LogicalWidth * scale),
            (int)Math.Round(LogicalHeight * scale),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        Assert.Equal((int)Math.Round(LogicalWidth * scale), bitmap.PixelWidth);
        Assert.Equal((int)Math.Round(LogicalHeight * scale), bitmap.PixelHeight);
        Assert.Equal(dpi, bitmap.DpiX, 3);
        Assert.Equal(dpi, bitmap.DpiY, 3);
        AssertHasVisibleContent(bitmap, state, dpi);

        var path = Path.Combine(outputDirectory, $"task05-{state}-{dpi}dpi.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        Assert.True(stream.Length > 5_000, $"{path} is unexpectedly small.");
    }

    private static void AssertRequiredStateControls(
        MainWindow window,
        FrameworkElement root,
        string state,
        int dpi)
    {
        foreach (var name in new[]
                 {
                     "BrandPanel", "BrandName", "BrandVersion", "BrandSubtitle",
                     "HeaderTitle", "HeaderStatus", "FooterText",
                 })
        {
            AssertRequired(window, root, name, state, dpi);
        }

        var form = Get(window, "FormPanel");
        var loading = Get(window, "LoadingPanel");
        var error = Get(window, "ErrorPanel");
        if (state is "login" or "register")
        {
            foreach (var name in new[]
                     {
                         "FormPanel", "LoginTabButton", "RegisterTabButton", "LoginIdLabel",
                         "LoginIdBox", "PasswordLabel", "PasswordInput", "RememberLoginIdBox",
                         "PrimaryActionButton",
                     })
            {
                AssertRequired(window, root, name, state, dpi);
            }

            Assert.Equal(Visibility.Collapsed, loading.Visibility);
            Assert.Equal(Visibility.Collapsed, error.Visibility);
            var action = Assert.IsType<Button>(Get(window, "PrimaryActionButton"));
            Assert.Equal(state == "login" ? "登录并启动" : "申请账号", action.Content);
        }
        else if (state == "loading")
        {
            AssertRequired(window, root, "LoadingPanel", state, dpi);
            AssertRequired(window, root, "LoadingProgress", state, dpi);
            Assert.Equal(Visibility.Collapsed, form.Visibility);
            Assert.Equal(Visibility.Collapsed, error.Visibility);
        }
        else
        {
            AssertRequired(window, root, "ErrorPanel", state, dpi);
            AssertRequired(window, root, "ErrorCard", state, dpi);
            AssertRequired(window, root, "RetryButton", state, dpi);
            Assert.Equal(Visibility.Collapsed, form.Visibility);
            Assert.Equal(Visibility.Collapsed, loading.Visibility);
        }
    }

    private static void AssertRequired(
        MainWindow window,
        FrameworkElement root,
        string name,
        string state,
        int dpi)
    {
        var element = Get(window, name);
        Assert.Equal(Visibility.Visible, element.Visibility);
        Assert.True(
            element.ActualWidth > 0 && element.ActualHeight > 0,
            $"{state} at {dpi} DPI did not lay out required {name}.");
        var bounds = element.TransformToAncestor(root).TransformBounds(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        AssertInside(root, bounds, $"required {name}", state, dpi);
    }

    private static FrameworkElement Get(MainWindow window, string name) =>
        Assert.IsAssignableFrom<FrameworkElement>(window.FindName(name));

    private static void AssertVisibleElementsStayInside(
        FrameworkElement root,
        string state,
        int dpi)
    {
        foreach (var element in Descendants(root).OfType<FrameworkElement>())
        {
            if (element.Visibility != Visibility.Visible
                || element.ActualWidth <= 0
                || element.ActualHeight <= 0)
            {
                continue;
            }

            var bounds = element.TransformToAncestor(root).TransformBounds(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            AssertInside(root, bounds, element.GetType().Name, state, dpi);
        }
    }

    private static void AssertInside(
        FrameworkElement root,
        Rect bounds,
        string element,
        string state,
        int dpi) =>
        Assert.True(
            bounds.Left >= -0.5
            && bounds.Top >= -0.5
            && bounds.Right <= root.ActualWidth + 0.5
            && bounds.Bottom <= root.ActualHeight + 0.5,
            $"{state} at {dpi} DPI clips {element}: {bounds}.");

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var descendant in Descendants(VisualTreeHelper.GetChild(root, index)))
            {
                yield return descendant;
            }
        }
    }

    private static void AssertHasVisibleContent(BitmapSource bitmap, string state, int dpi)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var colors = new HashSet<int>();
        var xStep = Math.Max(1, bitmap.PixelWidth / 40);
        var yStep = Math.Max(1, bitmap.PixelHeight / 30);
        for (var y = 0; y < bitmap.PixelHeight; y += yStep)
        {
            for (var x = 0; x < bitmap.PixelWidth; x += xStep)
            {
                var offset = (y * stride) + (x * 4);
                colors.Add(
                    pixels[offset]
                    | (pixels[offset + 1] << 8)
                    | (pixels[offset + 2] << 16)
                    | (pixels[offset + 3] << 24));
            }
        }

        Assert.True(colors.Count > 10, $"{state} at {dpi} DPI rendered as blank/flat content.");
    }

    private static LauncherViewModel CreateViewModel() =>
        new(
            new UnusedConfigurationSource(),
            new UnusedApiClientFactory(),
            new EmptyPreferencesStore(),
            new NeverGameLauncher(),
            NullLauncherEventLog.Instance);

    private sealed class UnusedConfigurationSource : ILauncherConfigurationSource
    {
        public LauncherConfiguration Read() => throw new InvalidOperationException();
    }

    private sealed class ValidConfigurationSource : ILauncherConfigurationSource
    {
        public LauncherConfiguration Read() =>
            new(new Uri("https://server.example/"));
    }

    private sealed class UnusedApiClientFactory : ILauncherApiClientFactory
    {
        public ILauncherApiClient Create(Uri baseUrl) => throw new InvalidOperationException();
    }

    private sealed class SingleApiClientFactory(ILauncherApiClient client) : ILauncherApiClientFactory
    {
        public ILauncherApiClient Create(Uri baseUrl) => client;
    }

    private sealed class FieldErrorApiClient : ILauncherApiClient
    {
        public Task<LauncherApiResult<LauncherBootstrap>> GetBootstrapAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new LauncherApiResult<LauncherBootstrap>(new("1", "ready", true)));

        public Task<LauncherApiResult<string>> RegisterAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<LauncherApiResult<LauncherLogin>> LoginAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken) => Task.FromResult(
                new LauncherApiResult<LauncherLogin>(
                    null,
                    "invalid_request",
                    "请修复输入。",
                    new Dictionary<string, string> { ["loginId"] = "账号格式错误。" }));

        public void Dispose() { }
    }

    private sealed class SuccessfulApiClient : ILauncherApiClient
    {
        public Task<LauncherApiResult<LauncherBootstrap>> GetBootstrapAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new LauncherApiResult<LauncherBootstrap>(new("1", "ready", true)));

        public Task<LauncherApiResult<string>> RegisterAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<LauncherApiResult<LauncherLogin>> LoginAsync(
            string loginId,
            string password,
            CancellationToken cancellationToken) => Task.FromResult(
                new LauncherApiResult<LauncherLogin>(
                    new(
                        loginId,
                        "safe_ticket-123",
                        DateTimeOffset.MaxValue,
                        new("127.0.0.1", 7001))));

        public void Dispose() { }
    }

    private sealed class EmptyPreferencesStore : ILauncherPreferencesStore
    {
        public ValueTask<LauncherPreferences> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(LauncherPreferences.Empty);

        public ValueTask SaveAsync(
            LauncherPreferences preferences,
            CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class SuccessfulPreferencesStore : ILauncherPreferencesStore
    {
        public ValueTask<LauncherPreferences> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(LauncherPreferences.Empty);

        public ValueTask SaveAsync(
            LauncherPreferences preferences,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class NeverGameLauncher : IGameLauncher
    {
        public GameLaunchResult Launch(LauncherLogin login) => throw new InvalidOperationException();
    }

    private sealed class SuccessfulGameLauncher : IGameLauncher
    {
        public GameLaunchResult Launch(LauncherLogin login) => new(true);
    }
}
