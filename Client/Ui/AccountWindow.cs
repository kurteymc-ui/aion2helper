using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// App menu → AION 2 Helper: signs the meter in to the player's Discord on aion2helper.com (see <see cref="Account"/>), shows who it is
/// signed in as, and signs it out. While a sign-in waits, the code is shown here and on the page the browser opened, and the window asks
/// the site every few seconds whether it was approved; closing the window stops asking.
/// </summary>
public sealed class AccountWindow : Window
{
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), FontSize = 13 };
    private readonly TextBlock _code = new() { FontFamily = new FontFamily("Consolas"), FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), FontSize = 12 };
    private readonly Button _signIn = new() { Content = "Sign in with Discord", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _openAgain = new() { Content = "Open the page again", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly Button _signOut = new() { Content = "Sign out", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _myFights = new() { Content = "My fights", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
    private readonly DispatcherTimer _timer = new();
    private Account.LinkStart? _pending;
    private int _failures;

    public AccountWindow()
    {
        Title = Brand.AppName + " account";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _code.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");

        var buttons = new WrapPanel();
        buttons.Children.Add(_signIn);
        buttons.Children.Add(_openAgain);
        buttons.Children.Add(_myFights);
        buttons.Children.Add(_signOut);
        var root = new StackPanel { Margin = new Thickness(18, 16, 18, 18) };
        root.Children.Add(_status);
        root.Children.Add(_code);
        root.Children.Add(_hint);
        root.Children.Add(buttons);
        Content = root;
        ThemedChrome.Apply(this);

        _signIn.Click += async (_, _) => await StartAsync();
        _openAgain.Click += (_, _) => Open(_pending?.Url);
        _myFights.Click += (_, _) => Open(Brand.Site + "/tools/discord#meter");
        _signOut.Click += async (_, _) =>
        {
            _signOut.IsEnabled = false;
            await Account.SignOutAsync();
            _signOut.IsEnabled = true;
            Draw(null);
        };
        _timer.Tick += async (_, _) => await PollAsync();
        Closed += (_, _) => _timer.Stop();
        Account.Changed += OnAccountChanged;
        Closed += (_, _) => Account.Changed -= OnAccountChanged;
        Draw(null);
        Loaded += async (_, _) =>
        {
            await Account.RefreshAsync();
            Draw(null);
        };
    }

    private void OnAccountChanged() => Dispatcher.BeginInvoke(new Action(() => Draw(null)));

    /// <summary>Draws the state: signed in, waiting for the approval, or signed out (with an optional message).</summary>
    private void Draw(string? message)
    {
        if (Account.SignedIn)
        {
            _pending = null;
            _timer.Stop();
            _status.Text = "Signed in" + (Account.Name is { Length: > 0 } name ? " as " + name : "") + ".";
            _hint.Text = "Fights you upload go to your AION 2 Helper account. You choose who sees each one on the site: only you, anyone with the link, or everyone. New fights get the choice set on the Discord page (Meter).";
            _code.Visibility = Visibility.Collapsed;
            _signIn.Visibility = Visibility.Collapsed;
            _openAgain.Visibility = Visibility.Collapsed;
            _signOut.Visibility = Visibility.Visible;
            _myFights.Visibility = Visibility.Visible;
            return;
        }

        _signOut.Visibility = Visibility.Collapsed;
        _myFights.Visibility = Visibility.Collapsed;
        if (_pending is { } p)
        {
            _status.Text = "Approve the sign-in in your browser. The page shows this code:";
            _code.Text = p.Code;
            _code.Visibility = Visibility.Visible;
            _hint.Text = message ?? "Sign in with Discord there if the site asks, then press Approve. This window notices it by itself.";
            _signIn.Visibility = Visibility.Collapsed;
            _openAgain.Visibility = Visibility.Visible;
            return;
        }

        _status.Text = "Not signed in.";
        _hint.Text = message ?? "Sign in with your Discord to upload fights to aion2helper.com. Every fight you upload is yours: you decide who can see it, and you can delete it.";
        _code.Visibility = Visibility.Collapsed;
        _signIn.Visibility = Visibility.Visible;
        _signIn.IsEnabled = true;
        _openAgain.Visibility = Visibility.Collapsed;
    }

    private async Task StartAsync()
    {
        _signIn.IsEnabled = false;
        (Account.LinkStart? start, string? error) = await Account.StartAsync();
        if (start is null)
        {
            Draw(error);
            return;
        }

        _pending = start;
        _failures = 0;
        Draw(null);
        Open(start.Url);
        _timer.Interval = TimeSpan.FromSeconds(start.Interval);
        _timer.Start();
    }

    private async Task PollAsync()
    {
        if (_pending is not { } p)
        {
            _timer.Stop();
            return;
        }

        if (DateTime.UtcNow > p.ExpiresAt)
        {
            _pending = null;
            Draw("The sign-in ran out. Press Sign in with Discord to try again.");
            return;
        }

        _timer.Stop();
        Account.LinkState state = await Account.PollAsync(p.Poll);
        if (_pending != p)
        {
            return;
        }

        switch (state)
        {
            case Account.LinkState.Linked:
                Draw(null);
                await Account.RefreshAsync();
                Draw(null);
                return;
            case Account.LinkState.Expired:
                _pending = null;
                Draw("The sign-in ran out or was used already. Press Sign in with Discord to try again.");
                return;
            case Account.LinkState.Failed:
                _failures++;
                Draw(_failures >= 3 ? "The site can’t be reached right now. Still trying…" : null);
                break;
            default:
                _failures = 0;
                break;
        }

        if (IsLoaded)
        {
            _timer.Start();
        }
    }

    private static void Open(string? url)
    {
        if (url is null || !url.StartsWith(Brand.Site + "/", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // no browser set up: the code is shown here, and the page is aion2helper.com/meter/link
        }
    }
}
