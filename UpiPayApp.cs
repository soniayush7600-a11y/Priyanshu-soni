// UpiPayApp_AllInOne.cs  -  the whole UPI payment app in ONE file (.NET MAUI, C# only, no XAML)
//
// SETUP (about 2 minutes):
//  1. Visual Studio > New Project > ".NET MAUI App". Name it  UpiPayApp  (keeps the namespace below valid).
//  2. DELETE these template files: App.xaml, App.xaml.cs, MainPage.xaml, MainPage.xaml.cs, MauiProgram.cs
//     (this file replaces all of them). Keep the Platforms folder.
//  3. Add this file to the project.
//  4. Right-click project > Manage NuGet Packages > install  QRCoder .
//  5. Android: in Platforms/Android/AndroidManifest.xml add, inside <manifest> and outside <application>:
//         <queries>
//           <intent><action android:name="android.intent.action.VIEW" /><data android:scheme="upi" /></intent>
//         </queries>
//     iOS: in Platforms/iOS/Info.plist add:
//         <key>LSApplicationQueriesSchemes</key>
//         <array><string>upi</string><string>gpay</string><string>phonepe</string><string>paytmmp</string></array>
//  6. Run on a REAL phone that has a UPI app installed (emulators have none).

using System.Globalization;
using System.Text.RegularExpressions;
using QRCoder;

namespace UpiPayApp;

// ───────────────────────── App entry ─────────────────────────

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() =>
        MauiApp.CreateBuilder().UseMauiApp<App>().Build();
}

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new Window(new NavigationPage(new MainPage()));
}

// ───────────────────────── Model + UPI logic ─────────────────────────

public record UpiPayment(
    string PayeeVpa,
    string PayeeName,
    decimal Amount,
    string Note,
    string TransactionRef)
{
    // Standard UPI deep link: upi://pay?pa=...&pn=...&am=...&cu=INR&tn=...&tr=...
    public string ToUri()
    {
        static string E(string s) => Uri.EscapeDataString(s);
        var amount = Amount.ToString("0.00", CultureInfo.InvariantCulture);
        return $"upi://pay?pa={E(PayeeVpa)}&pn={E(PayeeName)}&am={amount}" +
               $"&cu=INR&tn={E(Note)}&tr={E(TransactionRef)}";
    }

    public static bool IsValidVpa(string? vpa) =>
        Regex.IsMatch(vpa ?? "", @"^[\w.\-]{2,256}@[a-zA-Z]{2,64}$");
}

public static class UpiService
{
    // Opens the system's UPI app(s). Android shows a chooser; iOS opens the first handler.
    public static async Task<bool> PayAsync(UpiPayment payment)
    {
        var uri = new Uri(payment.ToUri());
        if (!await Launcher.Default.CanOpenAsync(uri))
            return false;
        return await Launcher.Default.OpenAsync(uri);
    }

    public static ImageSource CreateQr(UpiPayment payment)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payment.ToUri(), QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(12);
        return ImageSource.FromStream(() => new MemoryStream(png));
    }
}

// ───────────────────────── UI (built in code) ─────────────────────────

public class MainPage : ContentPage
{
    private readonly Entry _vpa = new() { Placeholder = "UPI ID (name@bank)", Keyboard = Keyboard.Email };
    private readonly Entry _name = new() { Placeholder = "Payee name" };
    private readonly Entry _amount = new() { Placeholder = "Amount (INR)", Keyboard = Keyboard.Numeric };
    private readonly Entry _note = new() { Placeholder = "Note" };
    private readonly Image _qr = new() { HeightRequest = 240, IsVisible = false };
    private readonly Label _status = new() { FontSize = 13 };

    public MainPage()
    {
        Title = "UPI Pay";

        var pay = new Button { Text = "Pay now" };
        pay.Clicked += OnPayClicked;

        var qr = new Button { Text = "Show QR code" };
        qr.Clicked += OnQrClicked;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 12,
                Children = { _vpa, _name, _amount, _note, pay, qr, _qr, _status }
            }
        };
    }

    private bool TryBuild(out UpiPayment payment)
    {
        payment = default!;
        var vpa = _vpa.Text?.Trim() ?? "";

        if (!UpiPayment.IsValidVpa(vpa))
        { _status.Text = "Enter a valid UPI ID, like name@bank."; return false; }

        if (string.IsNullOrWhiteSpace(_name.Text))
        { _status.Text = "Enter the payee's name."; return false; }

        if (!decimal.TryParse(_amount.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amt) || amt <= 0)
        { _status.Text = "Enter an amount greater than zero."; return false; }

        // In a real app, the order id comes from YOUR server.
        payment = new UpiPayment(vpa, _name.Text.Trim(), amt, _note.Text ?? "", Guid.NewGuid().ToString("N"));
        return true;
    }

    private async void OnPayClicked(object? sender, EventArgs e)
    {
        if (!TryBuild(out var p)) return;
        var ok = await UpiService.PayAsync(p);
        _status.Text = ok
            ? "Complete the payment in your UPI app. Confirm the result on your server."
            : "Couldn't open a UPI app. Is one installed?";
    }

    private void OnQrClicked(object? sender, EventArgs e)
    {
        if (!TryBuild(out var p)) return;
        _qr.Source = UpiService.CreateQr(p);
        _qr.IsVisible = true;
        _status.Text = "Scan this with any UPI app.";
    }
}

// NOTES
//  - iOS gives no reliable payment-result callback. Verify payments server-side (gateway webhook) using the `tr` order id.
//  - Amount and payee should come from your backend in a real merchant app.
//  - A standalone UPI app that holds accounts needs a PSP bank partnership and NPCI approval.
