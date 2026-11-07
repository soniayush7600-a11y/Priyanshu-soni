// UpiPayApp.cs  -  WALLET PROTOTYPE (PLAY MONEY ONLY). Replaces the old UpiPayApp.cs. Same project, same build.
//
// Features: sign up / log in (phone + 4-digit PIN), wallet balance, add money, send money to another
// user of this app, history, KYC levels (Minimum KYC limit 10,000 / Full KYC limit 2,00,000).
//
// IMPORTANT: Everything is stored on the phone only and the money is NOT real.
// To handle REAL money, a licensed partner (RBI-authorised PPI issuer) must hold the funds.
// All wallet logic goes through IWalletProvider, so a developer can later write a
// "PartnerWalletProvider" that calls the partner's API and change ONE line:  Wallet.P = ...

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
        new Window(new NavigationPage(new AuthPage()));
}

// ───────────────────────── Data ─────────────────────────

public class User
{
    public string Phone { get; set; } = "";
    public string Name { get; set; } = "";
    public string PinHash { get; set; } = "";
    public decimal Balance { get; set; }
    public bool FullKyc { get; set; }
}

public class Txn
{
    public DateTime When { get; set; }
    public string Phone { get; set; } = "";   // whose history this belongs to
    public string Kind { get; set; } = "";    // Added / Sent / Received
    public string Other { get; set; } = "";   // other party
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
}

public class Db
{
    public List<User> Users { get; set; } = new();
    public List<Txn> Txns { get; set; } = new();
}

public static class Limits
{
    public const decimal MinKyc = 10000m;
    public const decimal FullKyc = 200000m;
    public static decimal For(User u) => u.FullKyc ? FullKyc : MinKyc;
}

// ───────────────────────── Wallet provider (the swappable part) ─────────────────────────

public interface IWalletProvider
{
    (bool ok, string msg) Register(string name, string phone, string pin);
    User? Login(string phone, string pin);
    User? Get(string phone);
    (bool ok, string msg) AddMoney(string phone, decimal amount);
    (bool ok, string msg) Send(string fromPhone, string toPhone, decimal amount, string note);
    (bool ok, string msg) UpgradeKyc(string phone);
    List<Txn> History(string phone);
}

public static class Wallet
{
    // Later: replace with  new PartnerWalletProvider(...)  that calls a licensed partner's API.
    public static IWalletProvider P = new DemoWalletProvider();
}

public class DemoWalletProvider : IWalletProvider
{
    private const string Key = "walletdb";
    private Db _db;

    public DemoWalletProvider()
    {
        try
        {
            var s = Preferences.Default.Get(Key, "");
            _db = string.IsNullOrEmpty(s) ? new Db() : (JsonSerializer.Deserialize<Db>(s) ?? new Db());
        }
        catch { _db = new Db(); }
    }

    private void Save() => Preferences.Default.Set(Key, JsonSerializer.Serialize(_db));

    private static string Hash(string phone, string pin) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phone + ":" + pin)));

    public (bool ok, string msg) Register(string name, string phone, string pin)
    {
        if (string.IsNullOrWhiteSpace(name)) return (false, "Enter your name.");
        if (!Regex.IsMatch(phone, @"^[6-9]\d{9}$")) return (false, "Enter a valid 10-digit mobile number.");
        if (!Regex.IsMatch(pin, @"^\d{4}$")) return (false, "PIN must be 4 digits.");
        if (_db.Users.Any(u => u.Phone == phone)) return (false, "This number already has an account.");
        _db.Users.Add(new User { Phone = phone, Name = name.Trim(), PinHash = Hash(phone, pin) });
        Save();
        return (true, "Account created.");
    }

    public User? Login(string phone, string pin) =>
        _db.Users.FirstOrDefault(u => u.Phone == phone && u.PinHash == Hash(phone, pin));

    public User? Get(string phone) => _db.Users.FirstOrDefault(u => u.Phone == phone);

    public (bool ok, string msg) AddMoney(string phone, decimal amount)
    {
        var u = Get(phone);
        if (u == null) return (false, "User not found.");
        if (u.Balance + amount > Limits.For(u))
            return (false, $"Limit reached. Your {(u.FullKyc ? "Full" : "Minimum")} KYC wallet can hold up to {Ui.Money(Limits.For(u))}.");
        u.Balance += amount;
        _db.Txns.Add(new Txn { When = DateTime.Now, Phone = phone, Kind = "Added", Other = "Bank (demo)", Amount = amount });
        Save();
        return (true, $"{Ui.Money(amount)} added (play money).");
    }

    public (bool ok, string msg) Send(string fromPhone, string toPhone, decimal amount, string note)
    {
        var from = Get(fromPhone);
        var to = Get(toPhone);
        if (from == null) return (false, "User not found.");
        if (to == null) return (false, "That number is not registered in this app.");
        if (fromPhone == toPhone) return (false, "You can't send money to yourself.");
        if (from.Balance < amount) return (false, "Not enough balance.");
        if (to.Balance + amount > Limits.For(to)) return (false, "The receiver's wallet limit would be exceeded.");
        from.Balance -= amount;
        to.Balance += amount;
        var now = DateTime.Now;
        _db.Txns.Add(new Txn { When = now, Phone = fromPhone, Kind = "Sent", Other = to.Name + " (" + toPhone + ")", Amount = amount, Note = note });
        _db.Txns.Add(new Txn { When = now, Phone = toPhone, Kind = "Received", Other = from.Name + " (" + fromPhone + ")", Amount = amount, Note = note });
        Save();
        return (true, $"Sent {Ui.Money(amount)} to {to.Name}.");
    }

    public (bool ok, string msg) UpgradeKyc(string phone)
    {
        var u = Get(phone);
        if (u == null) return (false, "User not found.");
        u.FullKyc = true;   // Real apps: the licensed partner verifies ID documents / video KYC.
        Save();
        return (true, "Upgraded to Full KYC (demo).");
    }

    public List<Txn> History(string phone) =>
        _db.Txns.Where(t => t.Phone == phone).OrderByDescending(t => t.When).ToList();
}

// ───────────────────────── UI helpers ─────────────────────────

public static class Ui
{
    public static Entry Input(string placeholder, Keyboard? kb = null, bool password = false, int max = 0)
    {
        var e = new Entry { Placeholder = placeholder, Keyboard = kb ?? Keyboard.Default, IsPassword = password };
        if (max > 0) e.MaxLength = max;
        return e;
    }

    public static Button Btn(string text, EventHandler onClick)
    {
        var b = new Button { Text = text };
        b.Clicked += onClick;
        return b;
    }

    public static Label Title(string text) =>
        new Label { Text = text, FontSize = 22, FontAttributes = FontAttributes.Bold };

    public static Label Note(string text) =>
        new Label { Text = text, FontSize = 12, TextColor = Colors.Gray };

    public static string Money(decimal d) => "₹" + d.ToString("N2", new CultureInfo("en-IN"));

    public static bool TryAmount(string? s, out decimal amount) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0;

    public static ScrollView Page(params View[] children) =>
        new ScrollView { Content = new VerticalStackLayout { Padding = 20, Spacing = 12, Children = { } }.With(children) };

    private static VerticalStackLayout With(this VerticalStackLayout v, View[] children)
    {
        foreach (var c in children) v.Children.Add(c);
        return v;
    }
}

// ───────────────────────── Pages ─────────────────────────

public class AuthPage : ContentPage
{
    private bool _register;
    private readonly Entry _name = Ui.Input("Your name");
    private readonly Entry _phone = Ui.Input("Mobile number", Keyboard.Numeric, false, 10);
    private readonly Entry _pin = Ui.Input("4-digit PIN", Keyboard.Numeric, true, 4);
    private readonly Label _title = Ui.Title("");
    private readonly Label _msg = new() { TextColor = Colors.Red };
    private readonly Button _go;
    private readonly Button _toggle;

    public AuthPage()
    {
        Title = "Wallet Demo";
        _go = Ui.Btn("", OnGo);
        _toggle = new Button { BackgroundColor = Colors.Transparent, TextColor = Colors.Gray };
        _toggle.Clicked += (s, e) => { _register = !_register; Refresh(); };

        Content = Ui.Page(_title,
            Ui.Note("DEMO: play money only. No real money is stored or moved."),
            _name, _phone, _pin, _go, _toggle, _msg);
        Refresh();
    }

    private void Refresh()
    {
        _name.IsVisible = _register;
        _title.Text = _register ? "Create account" : "Log in";
        _go.Text = _register ? "Sign up" : "Log in";
        _toggle.Text = _register ? "I already have an account" : "Create new account";
        _msg.Text = "";
    }

    private void OnGo(object? sender, EventArgs e)
    {
        var phone = _phone.Text?.Trim() ?? "";
        var pin = _pin.Text?.Trim() ?? "";
        if (_register)
        {
            var r = Wallet.P.Register(_name.Text ?? "", phone, pin);
            if (!r.ok) { _msg.Text = r.msg; return; }
        }
        var user = Wallet.P.Login(phone, pin);
        if (user == null) { _msg.Text = "Wrong number or PIN."; return; }
        Application.Current!.Windows[0].Page = new NavigationPage(new HomePage(user.Phone));
    }
}

public class HomePage : ContentPage
{
    private readonly string _phone;
    private readonly Label _hello = Ui.Title("");
    private readonly Label _balance = new() { FontSize = 34, FontAttributes = FontAttributes.Bold };
    private readonly Label _kyc = Ui.Note("");

    public HomePage(string phone)
    {
        _phone = phone;
        Title = "My Wallet";
        Content = Ui.Page(
            _hello,
            Ui.Note("Wallet balance (play money)"),
            _balance, _kyc,
            Ui.Btn("Add money", (s, e) => Navigation.PushAsync(new AddPage(_phone))),
            Ui.Btn("Send money", (s, e) => Navigation.PushAsync(new SendPage(_phone))),
            Ui.Btn("History", (s, e) => Navigation.PushAsync(new HistoryPage(_phone))),
            Ui.Btn("KYC level", (s, e) => Navigation.PushAsync(new KycPage(_phone))),
            Ui.Btn("Log out", (s, e) => { Application.Current!.Windows[0].Page = new NavigationPage(new AuthPage()); }));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var u = Wallet.P.Get(_phone);
        if (u == null) return;
        _hello.Text = "Hi, " + u.Name;
        _balance.Text = Ui.Money(u.Balance);
        _kyc.Text = (u.FullKyc ? "Full KYC" : "Minimum KYC") + "  ·  wallet limit " + Ui.Money(Limits.For(u));
    }
}

public class AddPage : ContentPage
{
    public AddPage(string phone)
    {
        Title = "Add money";
        var amount = Ui.Input("Amount (₹)", Keyboard.Numeric);
        var msg = new Label { TextColor = Colors.Red };

        Content = Ui.Page(
            Ui.Title("Add money"),
            Ui.Note("Demo: this pretends to pull money from your bank. Nothing real moves."),
            amount,
            Ui.Btn("Add", async (s, e) =>
            {
                if (!Ui.TryAmount(amount.Text, out var a)) { msg.Text = "Enter a valid amount."; return; }
                var r = Wallet.P.AddMoney(phone, a);
                if (!r.ok) { msg.Text = r.msg; return; }
                await DisplayAlert("Done", r.msg, "OK");
                await Navigation.PopAsync();
            }),
            msg);
    }
}

public class SendPage : ContentPage
{
    public SendPage(string phone)
    {
        Title = "Send money";
        var to = Ui.Input("Receiver's mobile number", Keyboard.Numeric, false, 10);
        var amount = Ui.Input("Amount (₹)", Keyboard.Numeric);
        var note = Ui.Input("Note (optional)");
        var pin = Ui.Input("Your PIN", Keyboard.Numeric, true, 4);
        var msg = new Label { TextColor = Colors.Red };

        Content = Ui.Page(
            Ui.Title("Send money"),
            Ui.Note("The receiver must have an account in this app."),
            to, amount, note, pin,
            Ui.Btn("Send", async (s, e) =>
            {
                if (Wallet.P.Login(phone, pin.Text?.Trim() ?? "") == null) { msg.Text = "Wrong PIN."; return; }
                if (!Ui.TryAmount(amount.Text, out var a)) { msg.Text = "Enter a valid amount."; return; }
                var r = Wallet.P.Send(phone, to.Text?.Trim() ?? "", a, note.Text ?? "");
                if (!r.ok) { msg.Text = r.msg; return; }
                await DisplayAlert("Done", r.msg, "OK");
                await Navigation.PopAsync();
            }),
            msg);
    }
}

public class HistoryPage : ContentPage
{
    public HistoryPage(string phone)
    {
        Title = "History";
        var views = new List<View> { Ui.Title("Transactions") };
        var items = Wallet.P.History(phone);
        if (items.Count == 0) views.Add(Ui.Note("No transactions yet."));
        foreach (var t in items)
        {
            var sign = t.Kind == "Sent" ? "-" : "+";
            views.Add(new Label
            {
                Text = $"{t.Kind}  {sign}{Ui.Money(t.Amount)}\n{t.Other}" +
                       (string.IsNullOrWhiteSpace(t.Note) ? "" : $"  ·  {t.Note}") +
                       $"\n{t.When:dd MMM yyyy, hh:mm tt}",
                FontSize = 14
            });
        }
        Content = Ui.Page(views.ToArray());
    }
}

public class KycPage : ContentPage
{
    public KycPage(string phone)
    {
        Title = "KYC level";
        var u = Wallet.P.Get(phone);
        var status = new Label { FontSize = 18 };
        var msg = new Label();
        Button? upgrade = null;

        void Refresh()
        {
            var user = Wallet.P.Get(phone);
            status.Text = user != null && user.FullKyc
                ? "You are Full KYC. Wallet limit: " + Ui.Money(Limits.FullKyc)
                : "You are Minimum KYC. Wallet limit: " + Ui.Money(Limits.MinKyc);
            if (upgrade != null) upgrade.IsVisible = !(user != null && user.FullKyc);
        }

        upgrade = Ui.Btn("Upgrade to Full KYC (demo)", async (s, e) =>
        {
            var ok = await DisplayAlert("Demo KYC", "In a real app, a licensed partner checks your ID documents or video KYC. Upgrade for this demo?", "Upgrade", "Cancel");
            if (!ok) return;
            msg.Text = Wallet.P.UpgradeKyc(phone).msg;
            Refresh();
        });

        Content = Ui.Page(
            Ui.Title("KYC level"),
            status,
            Ui.Note("Minimum KYC: quick sign-up, lower limit (good for people without a bank account).\nFull KYC: ID verified, higher limit."),
            upgrade, msg);
        Refresh();
    }
}
